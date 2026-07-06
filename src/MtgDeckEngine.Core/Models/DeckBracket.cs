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
    bool IsEstimate = true);
