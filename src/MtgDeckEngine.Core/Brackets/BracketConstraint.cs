namespace MtgDeckEngine.Core.Brackets;

/// <summary>
/// The deck-composition rules a build must respect to legally sit at or below a
/// target Commander Bracket. Derived from WotC's published bracket definitions;
/// only the parts we can check from card names are represented here.
///
/// Brackets 4 (Optimized) and 5 (cEDH) impose no card restrictions, so a target
/// of 4+ leaves the builder unconstrained.
/// </summary>
public sealed record BracketConstraint(
    int TargetBracket,
    int MaxGameChangers,
    bool AllowMassLandDenial,
    int MaxExtraTurnSpells)
{
    public static BracketConstraint For(int targetBracket) => targetBracket switch
    {
        // Exhibition — ultra-casual, no Game Changers at all.
        1 => new BracketConstraint(1, MaxGameChangers: 0, AllowMassLandDenial: false, MaxExtraTurnSpells: 0),
        // Core — precon-level; Game Changers are still off the table.
        2 => new BracketConstraint(2, MaxGameChangers: 0, AllowMassLandDenial: false, MaxExtraTurnSpells: 0),
        // Upgraded — up to three Game Changers, still no mass land denial.
        3 => new BracketConstraint(3, MaxGameChangers: 3, AllowMassLandDenial: false, MaxExtraTurnSpells: 2),
        // Optimized / cEDH — unconstrained.
        _ => new BracketConstraint(Math.Clamp(targetBracket, 4, 5),
                 MaxGameChangers: int.MaxValue, AllowMassLandDenial: true,
                 MaxExtraTurnSpells: int.MaxValue),
    };

    public bool IsUnconstrained => MaxGameChangers == int.MaxValue && AllowMassLandDenial;

    public bool IsGameChanger(string cardName) => Matches(BracketEvaluator.GameChangers, cardName);
    public bool IsMassLandDenial(string cardName) => Matches(BracketEvaluator.MassLandDenial, cardName);
    public bool IsExtraTurn(string cardName) => Matches(BracketEvaluator.ExtraTurnSpells, cardName);

    /// <summary>Cards this constraint forbids outright, regardless of count.</summary>
    public bool IsBanned(string cardName)
        => (!AllowMassLandDenial && IsMassLandDenial(cardName))
        || (MaxExtraTurnSpells == 0 && IsExtraTurn(cardName))
        || (MaxGameChangers == 0 && IsGameChanger(cardName));

    private static bool Matches(IReadOnlySet<string> set, string name)
    {
        var trimmed = name.Trim();
        if (set.Contains(trimmed)) return true;
        var idx = trimmed.IndexOf(" // ", StringComparison.Ordinal);
        return idx > 0 && set.Contains(trimmed[..idx]);
    }
}
