namespace MtgDeckEngine.Core.Models;

/// <summary>
/// Estimated Commander Bracket (1–5) for a decklist, per WotC's official bracket
/// system. This is a <b>deterministic estimate</b> built from the rules we can
/// evaluate against name-matched data: the Game Changers list, mass land denial,
/// and extra-turn spells. It does <b>not</b> detect two-card infinite combos —
/// that needs a combo database (e.g. Commander Spellbook), which we don't ingest.
/// Treat <see cref="Level"/> as a floor, not a verdict.
/// </summary>
public sealed record DeckBracket(
    int Level,
    string Label,
    int GameChangerCount,
    IReadOnlyList<string> GameChangersFound,
    bool HasMassLandDenial,
    bool HasExtraTurns,
    IReadOnlyList<string> Reasons,
    bool IsEstimate = true,
    /// <summary>
    /// Two-card infinite combos found in the list, each as its participating
    /// card names. Structured rather than prose because the builder acts on it:
    /// a combo is a hard Bracket-4 trigger that no card-level flag reveals, so
    /// the only way to honour a lower bracket cap is to break the pairs.
    /// Empty from the local evaluator, which cannot see combos at all.
    /// </summary>
    IReadOnlyList<IReadOnlyList<string>>? TwoCardCombos = null);
