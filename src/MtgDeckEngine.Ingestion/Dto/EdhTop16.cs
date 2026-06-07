using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

// ----- GraphQL envelope ------------------------------------------------------

public sealed class GraphQlRequest
{
    [JsonPropertyName("query")] public string Query { get; set; } = "";
    [JsonPropertyName("variables")] public object? Variables { get; set; }
}

public sealed class GraphQlResponse<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }

    // EDHTop16 returns errors as a list of plain strings, while standard
    // GraphQL servers (Apollo, Hot Chocolate, etc.) return a list of
    // objects with `message`, `locations`, `path`, `extensions`. Accept both
    // by parsing as raw JsonElement and converting at read time.
    [JsonPropertyName("errors")] public List<System.Text.Json.JsonElement>? Errors { get; set; }

    public string? FirstErrorMessage()
    {
        if (Errors is not { Count: > 0 }) return null;
        var first = Errors[0];
        if (first.ValueKind == System.Text.Json.JsonValueKind.String)
            return first.GetString();
        if (first.ValueKind == System.Text.Json.JsonValueKind.Object
            && first.TryGetProperty("message", out var msg)
            && msg.ValueKind == System.Text.Json.JsonValueKind.String)
            return msg.GetString();
        return first.ToString();
    }
}

// ----- EDHTop16 schema slice we actually use --------------------------------

public sealed class EdhTop16CommanderStatsEnvelope
{
    [JsonPropertyName("commander")] public EdhTop16Commander? Commander { get; set; }
}

public sealed class EdhTop16Commander
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("colorId")] public string? ColorId { get; set; }
    [JsonPropertyName("stats")] public EdhTop16CommanderStats? Stats { get; set; }
    [JsonPropertyName("entries")] public EdhTop16EntryConnection? Entries { get; set; }
}

public sealed class EdhTop16CommanderStats
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("topCuts")] public int TopCuts { get; set; }
    [JsonPropertyName("metaShare")] public decimal MetaShare { get; set; }
    [JsonPropertyName("winRate")] public decimal WinRate { get; set; }
    [JsonPropertyName("conversionRate")] public decimal ConversionRate { get; set; }
}

public sealed class EdhTop16EntryConnection
{
    [JsonPropertyName("edges")] public List<EdhTop16EntryEdge> Edges { get; set; } = new();
    [JsonPropertyName("pageInfo")] public EdhTop16PageInfo? PageInfo { get; set; }
}

public sealed class EdhTop16EntryEdge
{
    [JsonPropertyName("node")] public EdhTop16Entry Node { get; set; } = new();
}

public sealed class EdhTop16Entry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("standing")] public int Standing { get; set; }
    [JsonPropertyName("wins")] public int Wins { get; set; }
    [JsonPropertyName("losses")] public int Losses { get; set; }
    [JsonPropertyName("draws")] public int Draws { get; set; }
    [JsonPropertyName("winsSwiss")] public int WinsSwiss { get; set; }
    [JsonPropertyName("lossesSwiss")] public int LossesSwiss { get; set; }
    [JsonPropertyName("winsBracket")] public int WinsBracket { get; set; }
    [JsonPropertyName("lossesBracket")] public int LossesBracket { get; set; }
    [JsonPropertyName("decklist")] public string? Decklist { get; set; }
    [JsonPropertyName("priceUsd")] public decimal? PriceUsd { get; set; }
    [JsonPropertyName("tournament")] public EdhTop16Tournament? Tournament { get; set; }
    [JsonPropertyName("player")] public EdhTop16Player? Player { get; set; }
    [JsonPropertyName("maindeck")] public List<EdhTop16Card>? Maindeck { get; set; }
}

public sealed class EdhTop16Tournament
{
    [JsonPropertyName("TID")] public string TID { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("tournamentDate")] public string? TournamentDate { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("topCut")] public int TopCut { get; set; }
    [JsonPropertyName("swissRounds")] public int SwissRounds { get; set; }
}

public sealed class EdhTop16Player
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("topdeckProfile")] public string? TopdeckProfile { get; set; }
}

public sealed class EdhTop16Card
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("oracleId")] public string? OracleId { get; set; }
}

public sealed class EdhTop16PageInfo
{
    [JsonPropertyName("hasNextPage")] public bool HasNextPage { get; set; }
    [JsonPropertyName("endCursor")] public string? EndCursor { get; set; }
}
