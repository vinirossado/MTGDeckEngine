using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

// POST /v2/tournaments body. Only the fields we actually use are typed.
public sealed class TopDeckSearchRequest
{
    [JsonPropertyName("game")] public string Game { get; set; } = "Magic: The Gathering";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("last")] public int? LastDays { get; set; }
    [JsonPropertyName("participantMin")] public int? ParticipantMin { get; set; }
    [JsonPropertyName("columns")] public string[]? Columns { get; set; }
    [JsonPropertyName("rounds")] public bool? Rounds { get; set; }
}

// Tournament response — see TopDeck.gg /api/docs for the full surface.
public sealed class TopDeckTournament
{
    [JsonPropertyName("TID")] public string TID { get; set; } = "";
    [JsonPropertyName("tournamentName")] public string Name { get; set; } = "";
    [JsonPropertyName("startDate")] public long? StartDate { get; set; }
    [JsonPropertyName("game")] public string? Game { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("topCut")] public int? TopCut { get; set; }
    [JsonPropertyName("swissNum")] public int? SwissRounds { get; set; }
    [JsonPropertyName("standings")] public List<TopDeckStanding>? Standings { get; set; }
}

public sealed class TopDeckStanding
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("standing")] public int? Standing { get; set; }
    [JsonPropertyName("decklist")] public string? Decklist { get; set; }
    [JsonPropertyName("deckObj")] public JsonElement? DeckObj { get; set; }
    [JsonPropertyName("wins")] public int? Wins { get; set; }
    [JsonPropertyName("draws")] public int? Draws { get; set; }
    [JsonPropertyName("losses")] public int? Losses { get; set; }
    [JsonPropertyName("winsSwiss")] public int? WinsSwiss { get; set; }
    [JsonPropertyName("lossesSwiss")] public int? LossesSwiss { get; set; }
    [JsonPropertyName("winsBracket")] public int? WinsBracket { get; set; }
    [JsonPropertyName("lossesBracket")] public int? LossesBracket { get; set; }
    [JsonPropertyName("winRate")] public decimal? WinRate { get; set; }
}
