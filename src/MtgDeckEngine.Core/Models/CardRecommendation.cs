namespace MtgDeckEngine.Core.Models;

public sealed record CardRecommendation(
    string OracleId,
    string Name,
    string? Category,
    decimal? InclusionPct,
    decimal? SynergyScore,
    decimal? PriceEur);
