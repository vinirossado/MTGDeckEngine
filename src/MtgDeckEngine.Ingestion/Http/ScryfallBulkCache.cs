using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Ingestion.Dto;

namespace MtgDeckEngine.Ingestion.Http;

/// <summary>
/// One-time download of Scryfall's "default_cards" bulk file. After load,
/// per-card lookups are dictionary hits, not HTTP calls — so ingestion runs
/// without rate-limit risk regardless of how many commanders we touch.
///
/// The bulk file lives in ~/.local/share/MtgDeckEngine/scryfall-default-cards.json
/// (or %LOCALAPPDATA% on Windows). Refresh policy: re-download if older than
/// <see cref="MaxAge"/> or missing.
/// </summary>
public sealed class ScryfallBulkCache(HttpClient http, ILogger<ScryfallBulkCache> logger)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Dictionary<string, ScryfallCard>? _byName;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public int Count => _byName?.Count ?? 0;

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_byName is not null) return;
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_byName is not null) return;

            var path = CachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var stale = !File.Exists(path) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)) > MaxAge;
            if (stale)
            {
                logger.LogInformation("Scryfall bulk cache missing/stale — downloading.");
                await DownloadBulkAsync(path, ct).ConfigureAwait(false);
            }
            else
            {
                logger.LogInformation("Scryfall bulk cache fresh — using local file.");
            }

            _byName = await LoadIntoIndexAsync(path, ct).ConfigureAwait(false);
            logger.LogInformation("Scryfall bulk cache loaded: {Count} cards.", _byName.Count);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public bool TryGetByName(string name, out ScryfallCard card)
    {
        if (_byName is not null && _byName.TryGetValue(Normalize(name), out var c))
        {
            card = c;
            return true;
        }
        card = default!;
        return false;
    }

    private async Task DownloadBulkAsync(string path, CancellationToken ct)
    {
        // Step 1: list bulk data sources.
        var index = await http.GetFromJsonAsync<ScryfallBulkDataIndex>("bulk-data", Json, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Scryfall returned empty bulk-data listing.");
        var entry = index.Data.FirstOrDefault(d => d.Type == "default_cards")
            ?? throw new InvalidOperationException("No 'default_cards' bulk entry available.");

        logger.LogInformation("Scryfall bulk: {Size:N0} bytes, updated {Updated:u}",
            entry.Size, entry.UpdatedAt);

        // Step 2: stream to disk (no full-buffering in memory).
        var tmp = path + ".tmp";
        using (var resp = await http.GetAsync(entry.DownloadUri,
                   HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dest = File.Create(tmp);
            await source.CopyToAsync(dest, ct).ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static async Task<Dictionary<string, ScryfallCard>> LoadIntoIndexAsync(
        string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var cards = await JsonSerializer.DeserializeAsync<List<ScryfallCard>>(fs, Json, ct)
            .ConfigureAwait(false)
            ?? new();

        // Prefer cards with an oracle_id (skips tokens etc.) and de-dupe by name —
        // multiple printings share an oracle_id, just keep the first we see.
        var dict = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.OracleId)) continue;
            dict.TryAdd(Normalize(c.Name), c);
        }
        return dict;
    }

    private static string Normalize(string name) => name.Trim();

    private static string CachePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        return Path.Combine(baseDir, "MtgDeckEngine", "scryfall-default-cards.json");
    }
}
