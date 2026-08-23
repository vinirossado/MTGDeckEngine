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
    DateOnly? LatestTopCutDate,
    /// <summary>
    /// Where the headline numbers came from. EDHTop16's aggregates cover the
    /// whole of their dataset; derived figures cover only the tournaments this
    /// graph has ingested, so they are a floor and not a meta share. Surfaced
    /// so a UI can say which it is showing rather than implying equivalence.
    /// </summary>
    CommanderMetaSource Source = CommanderMetaSource.None);

public enum CommanderMetaSource
{
    /// <summary>No tournament data at all for this commander.</summary>
    None,
    /// <summary>EDHTop16 commander-level aggregates.</summary>
    EdhTop16Aggregate,
    /// <summary>Computed from the tournament entries in this graph.</summary>
    DerivedFromEntries,
}
