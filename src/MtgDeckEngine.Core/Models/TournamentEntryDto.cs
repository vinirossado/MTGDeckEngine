namespace MtgDeckEngine.Core.Models;

public sealed record TournamentEntryDto(
    string Source,
    string TournamentId,
    string TournamentName,
    DateOnly Date,
    int PlayerCount,
    string PlayerName,
    string CommanderSlug,
    string? DeckSourceId,
    int Placement,
    int WinsSwiss,
    int LossesSwiss,
    int WinsBracket,
    int LossesBracket);
