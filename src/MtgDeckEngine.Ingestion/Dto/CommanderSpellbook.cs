using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

/// <summary>
/// Request body for <c>POST /estimate-bracket</c>. Cards are identified by name;
/// the backend resolves them itself (including non-English names).
/// </summary>
public sealed class SpellbookBracketRequest
{
    [JsonPropertyName("commanders")] public List<SpellbookCardRef> Commanders { get; set; } = new();
    [JsonPropertyName("main")] public List<SpellbookCardRef> Main { get; set; } = new();
}

public sealed class SpellbookCardRef
{
    [JsonPropertyName("card")] public string Card { get; set; } = "";
    [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
}

/// <summary>
/// Response of <c>POST /estimate-bracket</c>. The backend converts its snake_case
/// model to camelCase on the way out, so these names match the wire format.
/// </summary>
public sealed class SpellbookBracketResponse
{
    [JsonPropertyName("bracketTag")] public string? BracketTag { get; set; }
    [JsonPropertyName("cards")] public List<SpellbookCardResult> Cards { get; set; } = new();
    [JsonPropertyName("combos")] public List<SpellbookComboResult> Combos { get; set; } = new();
}

public sealed class SpellbookCardResult
{
    [JsonPropertyName("card")] public SpellbookCardInfo? Card { get; set; }
    [JsonPropertyName("banned")] public bool Banned { get; set; }
    [JsonPropertyName("gameChanger")] public bool GameChanger { get; set; }
    [JsonPropertyName("massLandDenial")] public bool MassLandDenial { get; set; }
    [JsonPropertyName("extraTurn")] public bool ExtraTurn { get; set; }
}

public sealed class SpellbookCardInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("oracleId")] public string? OracleId { get; set; }
}

public sealed class SpellbookComboResult
{
    [JsonPropertyName("combo")] public SpellbookCombo? Combo { get; set; }
    [JsonPropertyName("relevant")] public bool Relevant { get; set; }
    [JsonPropertyName("definitelyTwoCard")] public bool DefinitelyTwoCard { get; set; }
    [JsonPropertyName("arguablyTwoCard")] public bool ArguablyTwoCard { get; set; }
    /// <summary>Roughly "how fast this combo wins"; 4+ is a cEDH-speed line.</summary>
    [JsonPropertyName("speed")] public int Speed { get; set; }
    [JsonPropertyName("lock")] public bool Lock { get; set; }
    [JsonPropertyName("skipTurns")] public bool SkipTurns { get; set; }
}

public sealed class SpellbookCombo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("uses")] public List<SpellbookComboUse> Uses { get; set; } = new();
}

public sealed class SpellbookComboUse
{
    [JsonPropertyName("card")] public SpellbookCardInfo? Card { get; set; }
}
