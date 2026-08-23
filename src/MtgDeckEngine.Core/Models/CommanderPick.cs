namespace MtgDeckEngine.Core.Models;

/// <summary>
/// A commander surfaced by the discovery query: "what should I build for this
/// bracket and this budget?" — the inverse of the deck builder, which starts
/// from a commander you have already chosen.
/// </summary>
public sealed record CommanderPick(
    string CommanderSlug,
    string Name,
    int DeckCount,
    int TournamentWins,
    int TournamentLosses,
    /// <summary>Raw observed win rate across this commander's tournament decks, 0–1.</summary>
    decimal? WinRate,
    /// <summary>
    /// The win rate we can be 95% confident the commander is at least
    /// achieving (Wilson score lower bound). This is what the list is ordered
    /// by: it collapses toward zero on thin samples, so one 3-0 deck cannot
    /// outrank a commander with thirty events.
    /// </summary>
    decimal AdjustedWinRate,
    int TopCutCount,
    /// <summary>Cheapest observed tournament deck for this commander, in EUR.</summary>
    decimal? MinDeckPriceEur,
    /// <summary>
    /// Mean observed tournament deck price. Mean rather than median because
    /// SPARQL has no MEDIAN aggregate and computing one would mean pulling every
    /// deck price back per commander — so a single luxury build does drag it up.
    /// Read it alongside <see cref="MinDeckPriceEur"/>, which is what the budget
    /// filter actually matches on.
    /// </summary>
    decimal? AvgDeckPriceEur,
    /// <summary>
    /// Highest Game Changer count seen across this commander's decks, and the
    /// bracket floor that implies. Estimated from card flags, so it is a floor:
    /// two-card combos are not visible here.
    /// </summary>
    int MaxGameChangers,
    int EstimatedBracket,
    string? ImageUrl = null);
