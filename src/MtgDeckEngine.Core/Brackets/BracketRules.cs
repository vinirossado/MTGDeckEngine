namespace MtgDeckEngine.Core.Brackets;

/// <summary>
/// What a decklist alone can tell you about its Commander Bracket, applying
/// WotC's published rules.
///
/// The important limit: <b>brackets 4 and 5 have identical card-list rules.</b>
/// Both allow unlimited Game Changers, combos, mass land denial and extra
/// turns. What separates them is mindset — bracket 5 means playing with a
/// competitive, metagame-aware approach — and no analysis of a list can see
/// that. So this never returns 5. A deck reported as 4 may well be played as
/// cEDH; that is the pilot's call, not the card list's.
///
/// Bracket 1 is likewise not detectable: Exhibition and Core differ by theme
/// and intent, not by any rule a list violates. 2 is the floor.
/// </summary>
public static class BracketRules
{
    public const int Illegal = 0;
    public const int Floor = 2;
    public const int MaxDerivable = 4;

    /// <summary>
    /// A combo at or above this speed is treated as an early-game one. Bracket 3
    /// permits combos that only assemble late; anything that can win in the
    /// first several turns pushes to 4.
    /// </summary>
    public const int EarlyComboSpeed = 4;

    public sealed record Signals(
        int GameChangerCount,
        bool HasMassLandDenial,
        bool HasExtraTurns,
        int EarlyTwoCardCombos,
        int LateTwoCardCombos,
        bool HasBannedCards);

    public static (int Level, string Label) Evaluate(Signals s)
    {
        if (s.HasBannedCards)
            return (Illegal, "Illegal — contains banned cards");

        // Bracket 4 triggers: anything brackets 1–3 forbid outright.
        if (s.GameChangerCount > 3 || s.HasMassLandDenial || s.EarlyTwoCardCombos > 0)
            return (4, "Optimized");

        // Bracket 3 allows up to three Game Changers and late-game combos.
        if (s.GameChangerCount > 0 || s.LateTwoCardCombos > 0)
            return (3, "Upgraded");

        // Extra turns are permitted in low quantities at 2 and 3; several
        // suggest a chain, which belongs at 4.
        if (s.HasExtraTurns)
            return (3, "Upgraded");

        return (Floor, "Core");
    }

    /// <summary>
    /// Why a list cannot be graded above 4, phrased for a caller to surface.
    /// </summary>
    public const string CeilingNote =
        "Brackets 4 and 5 share the same card-list rules — the difference is a "
      + "competitive, metagame-aware mindset, which a decklist cannot show. This "
      + "deck is graded 4 by its cards; whether it is played as cEDH is up to you.";
}
