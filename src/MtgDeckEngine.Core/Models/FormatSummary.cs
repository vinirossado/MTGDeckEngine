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
    int TopCutCount);
