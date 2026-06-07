using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Ingestion.Dto;

namespace MtgDeckEngine.Ingestion.Http;

/// <summary>
/// Thin GraphQL client over <c>https://edhtop16.com/api/graphql</c>. No auth.
/// Pagination handled by the caller via the cursor in <see cref="EdhTop16PageInfo"/>.
/// </summary>
public sealed class EdhTop16Client(HttpClient http, ILogger<EdhTop16Client> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string CommanderStatsQuery = """
        query($name: String!, $period: TimePeriod!) {
          commander(name: $name) {
            name
            colorId
            stats(filters: { timePeriod: $period }) {
              count
              topCuts
              metaShare
              winRate
              conversionRate
            }
          }
        }
        """;

    private const string CommanderEntriesQuery = """
        query($name: String!, $first: Int!, $after: String) {
          commander(name: $name) {
            name
            entries(first: $first, after: $after, sortBy: TOP) {
              edges {
                node {
                  id
                  standing
                  wins
                  losses
                  draws
                  winsSwiss
                  lossesSwiss
                  winsBracket
                  lossesBracket
                  decklist
                  priceUsd
                  tournament {
                    TID
                    name
                    tournamentDate
                    size
                    topCut
                    swissRounds
                  }
                  player {
                    id
                    name
                    profile
                  }
                  maindeck {
                    name
                    oracleId
                  }
                }
              }
              pageInfo {
                hasNextPage
                endCursor
              }
            }
          }
        }
        """;

    public async Task<EdhTop16Commander?> GetCommanderStatsAsync(
        string commanderDisplayName,
        EdhTop16TimePeriod period,
        CancellationToken ct)
    {
        var result = await SendAsync<EdhTop16CommanderStatsEnvelope>(
            CommanderStatsQuery,
            new { name = commanderDisplayName, period = period.ToString() },
            ct).ConfigureAwait(false);
        return result?.Commander;
    }

    /// <summary>
    /// Streams every tournament entry for the commander, paging by EDHTop16's
    /// connection cursor. Caller can break early; this is an async-iterator.
    /// </summary>
    public async IAsyncEnumerable<EdhTop16Entry> StreamEntriesAsync(
        string commanderDisplayName,
        int pageSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? cursor = null;
        var safetyCap = 200; // never page forever
        while (safetyCap-- > 0)
        {
            var envelope = await SendAsync<EdhTop16CommanderStatsEnvelope>(
                CommanderEntriesQuery,
                new { name = commanderDisplayName, first = pageSize, after = cursor },
                ct).ConfigureAwait(false);

            var conn = envelope?.Commander?.Entries;
            if (conn is null) yield break;

            foreach (var edge in conn.Edges)
                yield return edge.Node;

            if (conn.PageInfo?.HasNextPage != true) yield break;
            cursor = conn.PageInfo.EndCursor;
            if (string.IsNullOrEmpty(cursor)) yield break;
        }
    }

    private async Task<T?> SendAsync<T>(string query, object variables, CancellationToken ct)
    {
        var req = new GraphQlRequest { Query = query, Variables = variables };
        logger.LogDebug("EDHTop16 GraphQL → {Bytes} bytes", query.Length);

        // Explicit absolute URL: HttpClient.BaseAddress without a trailing
        // slash + PostAsync("") is ambiguous in some runtimes; the absolute
        // form is unambiguous.
        var resp = await http.PostAsJsonAsync(
            new Uri("https://edhtop16.com/api/graphql"),
            req, Json, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            logger.LogError("EDHTop16 HTTP {Status}: {Body}", (int)resp.StatusCode, errBody);
            resp.EnsureSuccessStatusCode();
        }

        var body = await resp.Content.ReadFromJsonAsync<GraphQlResponse<T>>(Json, ct).ConfigureAwait(false);
        if (body is null) return default;
        if (body.Errors is { Count: > 0 })
        {
            var first = body.Errors[0].Message;
            logger.LogWarning("EDHTop16 GraphQL errors: {Error}", first);
            throw new InvalidOperationException($"EDHTop16 returned GraphQL errors: {first}");
        }
        return body.Data;
    }
}

public enum EdhTop16TimePeriod
{
    ALL_TIME,
    ONE_MONTH,
    THREE_MONTHS,
    SIX_MONTHS,
    ONE_YEAR,
    POST_BAN
}
