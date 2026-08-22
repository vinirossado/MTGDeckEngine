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
    string? ColorIdentity = null,
    // Aggregate match record of every tournament deck (for this commander) that
    // played the card — Swiss + bracket combined, from EDHTop16.
    int? TournamentWins = null,
    int? TournamentLosses = null,
    // WotC Game Changer, straight from Scryfall's per-card flag. Drives the
    // bracket cap, which is why it comes from data rather than a curated list.
    bool IsGameChanger = false)
{
    public int? TournamentGames
        => TournamentWins is null && TournamentLosses is null
            ? null
            : (TournamentWins ?? 0) + (TournamentLosses ?? 0);

    /// <summary>
    /// Raw observed win rate, 0–1. Null when the card has no recorded games.
    /// Prefer <see cref="DeckRecommendationService"/>'s shrunk estimate for
    /// ranking — a 3-0 card seen in one deck is not better than a 60% card seen
    /// in thirty.
    /// </summary>
    public decimal? WinRate
        => TournamentGames is > 0
            ? (decimal)(TournamentWins ?? 0) / TournamentGames.Value
            : null;
}
