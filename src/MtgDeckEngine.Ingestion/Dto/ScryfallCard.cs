using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

public sealed class ScryfallCard
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("oracle_id")] public string? OracleId { get; set; }
    [JsonPropertyName("colors")] public List<string>? Colors { get; set; }
    [JsonPropertyName("color_identity")] public List<string>? ColorIdentity { get; set; }
    [JsonPropertyName("type_line")] public string? TypeLine { get; set; }
    [JsonPropertyName("oracle_text")] public string? OracleText { get; set; }
    [JsonPropertyName("prices")] public ScryfallPrices? Prices { get; set; }
    [JsonPropertyName("legalities")] public Dictionary<string, string>? Legalities { get; set; }
    [JsonPropertyName("image_uris")] public ScryfallImageUris? ImageUris { get; set; }
}

public sealed class ScryfallImageUris
{
    [JsonPropertyName("small")] public string? Small { get; set; }
    [JsonPropertyName("normal")] public string? Normal { get; set; }
    [JsonPropertyName("large")] public string? Large { get; set; }
    [JsonPropertyName("png")] public string? Png { get; set; }
    [JsonPropertyName("art_crop")] public string? ArtCrop { get; set; }
}

public sealed class ScryfallPrices
{
    [JsonPropertyName("eur")] public string? Eur { get; set; }
    [JsonPropertyName("usd")] public string? Usd { get; set; }
}

public sealed class ScryfallSearchPage
{
    [JsonPropertyName("data")] public List<ScryfallCard> Data { get; set; } = new();
    [JsonPropertyName("has_more")] public bool HasMore { get; set; }
    [JsonPropertyName("next_page")] public string? NextPage { get; set; }
}
