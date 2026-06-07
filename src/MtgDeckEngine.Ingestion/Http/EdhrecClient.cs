using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Ingestion.Dto;

namespace MtgDeckEngine.Ingestion.Http;

public sealed class EdhrecClient(HttpClient http, ILogger<EdhrecClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<EdhrecPage?> GetCommanderPageAsync(string slug, CancellationToken ct)
    {
        var url = $"pages/commanders/{slug}.json";
        logger.LogDebug("EDHREC GET {Url}", url);
        var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EdhrecPage>(Json, ct).ConfigureAwait(false);
    }
}
