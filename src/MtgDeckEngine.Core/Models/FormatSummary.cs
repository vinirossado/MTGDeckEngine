namespace MtgDeckEngine.Core.Models;

public sealed record FormatSummary(
    string Format,
    int TournamentCount,
    int DeckCount,
    int EntryCount);

public sealed record FormatMeta(
    string Format,
    int TournamentCount,
    int DeckCount,
    int EntryCount,
    int Top4DeckCount,
    int Top16DeckCount,
    DateOnly? LatestTournamentDate);

public sealed record FormatStaple(
    string OracleId,
    string Name,
    decimal? PriceEur,
    int DeckCount,
    string? ImageUrl = null);

public sealed record CommanderSummary(
    string CommanderSlug,
    string Name,
    int TournamentEntryCount,
    int TopCutCount,
    /// <summary>Tournament decks in the graph for this commander.</summary>
    int DeckCount = 0,
    /// <summary>
    /// Whether EDHREC data was ingested for this commander specifically, i.e.
    /// someone asked for it by name. Drives ordering: a commander you just
    /// ingested must be findable, however little tournament data it has.
    /// </summary>
    bool HasEdhrecData = false);
