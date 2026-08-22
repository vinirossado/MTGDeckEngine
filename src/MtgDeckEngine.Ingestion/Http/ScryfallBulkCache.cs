using System.IO.Compression;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core;
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
    private Dictionary<string, ScryfallCard>? _bySlug;
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

            (_byName, _bySlug) = await LoadIntoIndexAsync(path, ct).ConfigureAwait(false);
            logger.LogInformation(
                "Scryfall bulk cache loaded: {Count} cards (indexed by name + slug).",
                _byName.Count);
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

    /// <summary>
    /// Resolve a kebab-case slug (the same slug format used by EDHREC, e.g.
    /// "xyris-the-writhing-storm") back to the canonical Scryfall card. Slug
    /// → display name is lossy (commas and case are stripped), so we maintain
    /// a separate slug index built at load time.
    /// </summary>
    public bool TryGetBySlug(string slug, out ScryfallCard card)
    {
        if (_bySlug is not null && _bySlug.TryGetValue(slug, out var c))
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

        var (uri, gzipped) = entry.ResolveDownload();
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException(
                "Scryfall 'default_cards' entry has no download URI.");

        logger.LogInformation(
            "Scryfall bulk: {Size:N0} bytes, updated {Updated:u}, gzipped JSONL {Gzipped}",
            entry.ReportedSize, entry.UpdatedAt, gzipped);

        // Step 2: stream to disk (no full-buffering in memory). The JSONL feed
        // arrives gzipped, so decompress on the way through — the on-disk cache
        // is always plain text, one card object per line.
        var tmp = path + ".tmp";
        using (var resp = await http.GetAsync(uri,
                   HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dest = File.Create(tmp);
            if (gzipped)
            {
                await using var gz = new GZipStream(source, CompressionMode.Decompress);
                await gz.CopyToAsync(dest, ct).ConfigureAwait(false);
            }
            else
            {
                await source.CopyToAsync(dest, ct).ConfigureAwait(false);
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static async Task<(Dictionary<string, ScryfallCard> ByName,
                                Dictionary<string, ScryfallCard> BySlug)>
        LoadIntoIndexAsync(string path, CancellationToken ct)
    {
        // The cache file is JSON Lines (one card per line) since Scryfall moved
        // off the plain array feed. Older caches may still hold a JSON array —
        // sniff the first non-whitespace byte and pick the reader accordingly.
        var cards = await IsJsonArrayAsync(path, ct).ConfigureAwait(false)
            ? await ReadJsonArrayAsync(path, ct).ConfigureAwait(false)
            : await ReadJsonLinesAsync(path, ct).ConfigureAwait(false);

        // Group every printing of a card together, then pick one printing to
        // represent it. The bulk feed lists all printings, and which one you
        // pick decides the price the whole budget engine sees.
        var byName = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);
        var bySlug = new Dictionary<string, ScryfallCard>(StringComparer.Ordinal);

        foreach (var group in cards
                     .Where(c => !string.IsNullOrEmpty(c.OracleId))
                     .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var chosen = PickPrinting(group.ToList());
            byName[Normalize(chosen.Name)] = chosen;
            bySlug.TryAdd(MtgVocab.Slugify(chosen.Name), chosen);
        }
        return (byName, bySlug);
    }

    // A printing priced under this fraction of the card's median printing price
    // is discarded as bad data. Loose enough to keep genuine cheap reprints of
    // cards that also have pricey collector printings.
    private const decimal OutlierFloorRatio = 0.05m;

    /// <summary>
    /// Choose the printing that best represents "what this card costs to buy".
    ///
    /// Two traps live in this data. First, many printings are unpriced — gold-
    /// bordered World Championship decks, oversized memorabilia, digital-only
    /// cards — and picking one makes an expensive card look free (Tropical
    /// Island resolves to an unpriced "olgc" printing). Second, Scryfall
    /// occasionally carries a bogus near-zero listing: Wheel of Fortune's
    /// Summer Magic printing sits at EUR 0.02 against a ~EUR 280 median.
    /// Either one lets the budget builder buy a staple for nothing.
    /// </summary>
    private static ScryfallCard PickPrinting(List<ScryfallCard> printings)
    {
        var priced = printings
            .Where(p => p.IsPurchasablePaper && p.BestPrice is > 0m)
            .OrderBy(p => p.BestPrice!.Value)
            .ToList();

        // Nothing purchasable and priced — fall back to any paper printing so
        // the card still resolves for name/oracle-id lookups. It stays unpriced,
        // and the budget builder excludes unpriced cards by design.
        if (priced.Count == 0)
            return printings.FirstOrDefault(p => p.IsPurchasablePaper) ?? printings[0];

        // The cheapest printing is the right budget answer, so long as it is a
        // real price. Anything far below the median of this card's own printings
        // is treated as a data error rather than a bargain.
        var median = priced[priced.Count / 2].BestPrice!.Value;
        var floor = median * OutlierFloorRatio;
        return priced.FirstOrDefault(p => p.BestPrice!.Value >= floor) ?? priced[^1];
    }

    private static async Task<bool> IsJsonArrayAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var buf = new byte[16];
        var read = await fs.ReadAsync(buf, ct).ConfigureAwait(false);
        for (var i = 0; i < read; i++)
        {
            if (char.IsWhiteSpace((char)buf[i])) continue;
            return buf[i] == (byte)'[';
        }
        return false;
    }

    private static async Task<List<ScryfallCard>> ReadJsonArrayAsync(
        string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ScryfallCard>>(fs, Json, ct)
            .ConfigureAwait(false) ?? new();
    }

    private static async Task<List<ScryfallCard>> ReadJsonLinesAsync(
        string path, CancellationToken ct)
    {
        var cards = new List<ScryfallCard>(capacity: 100_000);
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0) continue;
            // Tolerate a stray array wrapper / trailing comma if the feed shape
            // shifts again — a malformed line must not kill the whole ingest.
            var trimmed = line.Trim().TrimEnd(',');
            if (trimmed is "" or "[" or "]") continue;
            try
            {
                if (JsonSerializer.Deserialize<ScryfallCard>(trimmed, Json) is { } card)
                    cards.Add(card);
            }
            catch (JsonException)
            {
                // skip unparseable line
            }
        }
        return cards;
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
