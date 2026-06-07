using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

public sealed class EdhrecPage
{
    [JsonPropertyName("container")] public EdhrecContainer? Container { get; set; }
}

public sealed class EdhrecContainer
{
    [JsonPropertyName("json_dict")] public EdhrecJsonDict? JsonDict { get; set; }
}

public sealed class EdhrecJsonDict
{
    [JsonPropertyName("cardlists")] public List<EdhrecCardlist>? Cardlists { get; set; }
}

public sealed class EdhrecCardlist
{
    [JsonPropertyName("header")] public string? Header { get; set; }
    [JsonPropertyName("tag")] public string? Tag { get; set; }
    [JsonPropertyName("cardviews")] public List<EdhrecCardview>? Cardviews { get; set; }
}

public sealed class EdhrecCardview
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sanitized")] public string? Sanitized { get; set; }
    [JsonPropertyName("num_decks")] public int NumDecks { get; set; }
    [JsonPropertyName("potential_decks")] public int PotentialDecks { get; set; }
    [JsonPropertyName("synergy")] public decimal Synergy { get; set; }
    [JsonPropertyName("inclusion")] public decimal? Inclusion { get; set; }
    [JsonPropertyName("salt")] public decimal? Salt { get; set; }
}
