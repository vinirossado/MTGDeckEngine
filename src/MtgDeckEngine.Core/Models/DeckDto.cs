namespace MtgDeckEngine.Core.Models;

public sealed record DeckDto(
    string Source,
    string SourceId,
    string CommanderSlug,
    IReadOnlyList<string> CardOracleIds,
    decimal? TotalPriceEur);
