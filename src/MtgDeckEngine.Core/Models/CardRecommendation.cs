namespace MtgDeckEngine.Core.Models;

public sealed record CardRecommendation(
    string OracleId,
    string Name,
    string? Category,
    decimal? InclusionPct,
    decimal? SynergyScore,
    decimal? PriceEur,
    int? TopCutAppearances = null,
    string? ImageUrl = null,
    string? TypeLine = null,
    // Scryfall colour identity as a comma-joined WUBRG string, e.g. "R,G,U".
    string? ColorIdentity = null);
