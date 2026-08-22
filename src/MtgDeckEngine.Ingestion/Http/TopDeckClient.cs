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

            using var resp = await http.PostAsync(
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

            // TopDeck answers 200 with an EMPTY body when a query matches nothing
            // (and, for wide queries, when it gives up after ~30s). Feeding that
            // straight to ReadFromJsonAsync throws "input does not contain any
            // JSON tokens", which surfaced as a stack trace on every startup for
            // what is really just "no tournaments".
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                logger.LogInformation(
                    "TopDeck: empty response for {Format} (last {Last}d, ≥{Min} players) — treating as no results.",
                    filter.Format, filter.LastDays, filter.ParticipantMin);
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<TopDeckTournament>>(body, Json) ?? [];
            }
            catch (JsonException ex)
            {
                // A 200 carrying non-JSON (an HTML error/maintenance page) is a
                // problem with the upstream response, not with our data — log it
                // and let ingestion carry on with the other formats.
                logger.LogWarning(ex,
                    "TopDeck: unparseable 200 response for {Format} ({Bytes} bytes); skipping this format.",
                    filter.Format, body.Length);
                return [];
            }
        }
    }
}
