namespace MtgDeckEngine.Core.Models;

/// <summary>
/// Tournament-derived signals for a commander, aggregated from EDHTop16
/// (and, in Phase 3, fused with EDHREC popularity).
/// </summary>
public sealed record CommanderMeta(
    string CommanderSlug,
    int TournamentEntryCount,
    int TopCutCount,
    decimal? WinRate,
    decimal? ConversionRate,
    decimal? MetaShare,
    int Top4DeckCount,
    int Top16DeckCount,
    DateOnly? LatestTopCutDate);
