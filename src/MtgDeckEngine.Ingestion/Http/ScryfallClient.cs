using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Ingestion.Dto;

namespace MtgDeckEngine.Ingestion.Http;

public sealed class ScryfallClient(HttpClient http, ILogger<ScryfallClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ScryfallCard?> GetByNameExactAsync(string name, CancellationToken ct)
    {
        var url = $"cards/named?exact={Uri.EscapeDataString(name)}";
        return await SendWithRetryAsync<ScryfallCard>(url, ct).ConfigureAwait(false);
    }

    private async Task<T?> SendWithRetryAsync<T>(string url, CancellationToken ct, int maxAttempts = 5)
        where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            logger.LogDebug("Scryfall GET {Url} (attempt {Attempt})", url, attempt);
            var resp = await http.GetAsync(url, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxAttempts)
            {
                var wait = resp.Headers.RetryAfter?.Delta
                           ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                logger.LogWarning("Scryfall 429 — backing off {Wait}s (attempt {Attempt}/{Max})",
                    wait.TotalSeconds, attempt, maxAttempts);
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Streams every page of a Scryfall search query. Respects the rate-limit hint
    /// of 50-100ms between requests with a small inter-page delay.
    /// </summary>
    public async IAsyncEnumerable<ScryfallCard> SearchAsync(
        string query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"cards/search?q={Uri.EscapeDataString(query)}";
        while (!string.IsNullOrEmpty(url))
        {
            logger.LogDebug("Scryfall GET {Url}", url);
            var page = await http.GetFromJsonAsync<ScryfallSearchPage>(url, Json, ct).ConfigureAwait(false);
            if (page is null) yield break;
            foreach (var card in page.Data) yield return card;
            url = page.HasMore ? page.NextPage ?? "" : "";
            if (!string.IsNullOrEmpty(url))
                await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }
}
