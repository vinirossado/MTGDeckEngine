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

    // Printing-level fields. The bulk "default_cards" feed carries one arbitrary
    // printing per card, and some of them are not purchasable products (gold-
    // bordered World Championship decks, oversized memorabilia, digital-only
    // printings). Those carry all-null prices, so picking one makes an expensive
    // card look free. See PricePreference in ScryfallBulkCache.
    // WotC's official Commander "Game Changer" flag, published per card by
    // Scryfall. Reading it here keeps the list current automatically instead of
    // relying on a curated copy that silently goes stale.
    [JsonPropertyName("game_changer")] public bool GameChanger { get; set; }

    [JsonPropertyName("digital")] public bool Digital { get; set; }
    [JsonPropertyName("set_type")] public string? SetType { get; set; }
    [JsonPropertyName("border_color")] public string? BorderColor { get; set; }
    [JsonPropertyName("games")] public List<string>? Games { get; set; }

    /// <summary>Cheapest known price for this printing, EUR preferred then USD.</summary>
    public decimal? BestPrice
    {
        get
        {
            if (decimal.TryParse(Prices?.Eur, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var e)) return e;
            if (decimal.TryParse(Prices?.Usd, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var u)) return u;
            return null;
        }
    }

    /// <summary>
    /// Whether this printing is a real paper product someone could buy. Gold-
    /// bordered and memorabilia sets are never priced; digital-only printings
    /// are priced in tickets, not currency.
    /// </summary>
    public bool IsPurchasablePaper
        => !Digital
        && !string.Equals(BorderColor, "gold", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(SetType, "memorabilia", StringComparison.OrdinalIgnoreCase)
        && (Games is null || Games.Count == 0 || Games.Contains("paper"));
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
