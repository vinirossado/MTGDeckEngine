using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Ingestion.Dto;

namespace MtgDeckEngine.Ingestion.Http;

/// <summary>
/// REST client for the TopDeck.gg v2 API. Auth is a bare API key in the
/// <c>Authorization</c> header (no <c>Bearer</c> prefix per their docs).
/// Rate limit is 100 req/min on standard endpoints; we honour
/// <c>Retry-After</c> on 429 with bounded retries.
/// </summary>
public sealed class TopDeckClient(HttpClient http, ILogger<TopDeckClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Search completed tournaments matching the filters. Returns the parsed
    /// list (TopDeck wraps results in a flat JSON array, not an envelope).
    /// </summary>
    public async Task<IReadOnlyList<TopDeckTournament>> SearchTournamentsAsync(
        TopDeckSearchRequest filter,
        CancellationToken ct,
        int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payload = JsonSerializer.Serialize(filter, Json);
            using var content = new StringContent(payload);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            logger.LogDebug("TopDeck POST /v2/tournaments (format={Format}, last={Last}d)",
                filter.Format, filter.LastDays);

            var resp = await http.PostAsync(
                new Uri("https://topdeck.gg/api/v2/tournaments"),
                content, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxAttempts)
            {
                var wait = resp.Headers.RetryAfter?.Delta
                           ?? TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
                logger.LogWarning("TopDeck 429 — backing off {Wait}s (attempt {Attempt}/{Max})",
                    wait.TotalSeconds, attempt, maxAttempts);
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                logger.LogError("TopDeck HTTP {Status}: {Body}", (int)resp.StatusCode, errBody);
                resp.EnsureSuccessStatusCode();
            }

            var list = await resp.Content
                .ReadFromJsonAsync<List<TopDeckTournament>>(Json, ct)
                .ConfigureAwait(false);
            return list ?? new();
        }
    }
}
