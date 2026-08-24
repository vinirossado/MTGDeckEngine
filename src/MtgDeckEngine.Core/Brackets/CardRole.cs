using System.Text.RegularExpressions;

namespace MtgDeckEngine.Core.Brackets;

/// <summary>What a card does for the deck, for quota purposes.</summary>
public enum CardRole
{
    Land,
    Ramp,
    Draw,
    Removal,
    Creature,
    Other,
}

/// <summary>
/// Classifies a card's role from its type line and oracle text.
///
/// EDHREC's commander pages do not supply functional roles — their categories
/// are card-type sections (Instants, Sorceries, Mana Artifacts, Top Cards).
/// Matching those against "ramp", "card draw" and "removal", which the previous
/// bucket logic did, therefore never matched anything: every nonland
/// non-creature fell into Other, and the ramp/draw/removal quotas silently did
/// nothing.
///
/// Oracle text is available for ~91% of cards and does carry the signal. These
/// are keyword heuristics, not a rules engine: they will miss a card whose
/// effect is worded unusually and will occasionally claim one that only reads
/// like ramp. Good enough to make quotas bind and to describe a deck's shape;
/// not something to quote as fact about a single card.
/// </summary>
public static class CardRoleClassifier
{
    public static CardRole Classify(string? typeLine, string? oracleText)
    {
        var type = typeLine ?? "";
        if (type.Contains("Land", StringComparison.OrdinalIgnoreCase))
            return CardRole.Land;

        var text = (oracleText ?? "").ToLowerInvariant();
        var isCreature = type.Contains("Creature", StringComparison.OrdinalIgnoreCase);

        // Ramp and removal are checked before the creature type: a mana dork is
        // ramp that happens to be a creature, and a removal creature is still
        // how the deck answers things. Draw is checked after, since most
        // creatures that replace themselves are not "card draw" slots.
        if (IsRamp(text)) return CardRole.Ramp;
        if (IsRemoval(text)) return CardRole.Removal;
        if (isCreature) return CardRole.Creature;
        if (IsDraw(text)) return CardRole.Draw;

        return CardRole.Other;
    }

    // Mana abilities. The cost is not always a tap — Phyrexian Altar sacrifices
    // a creature — so match the ": add" shape rather than "{t}: add".
    private static readonly Regex RampPattern = new(
        @":\s*add\s*(\{|one mana|two mana|three mana|mana of)",
        RegexOptions.Compiled);

    // Land fetch only counts as ramp when the land reaches the battlefield; a
    // tutor that puts it in hand does not accelerate anything.
    private static readonly Regex LandRampPattern = new(
        @"search your library.*land.*onto the battlefield",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // "draw a card" through "draws seven cards", plus the wheel wordings:
    // "draws cards equal to", "draws that many cards".
    private static readonly Regex DrawPattern = new(
        @"draws?\s+(a|one|two|three|four|five|six|seven|x|that many)\s+cards?"
      + @"|draws?\s+cards\s+equal\s+to",
        RegexOptions.Compiled);

    // A wheel refills every hand. For an archetype built on them — Xyris being
    // the obvious case — calling them Other would misdescribe the whole deck.
    private static readonly Regex WheelPattern = new(
        @"(discards? their hand|shuffles? .*hand .*into .*library).*draws?",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RemovalPattern = new(
        @"destroy (target|all|each)"
      + @"|exile (target|all|each)"
      + @"|counter target"
      + @"|return target (nonland )?(creature|permanent|artifact|enchantment)"
      + @"|sacrifices? an? (creature|permanent|artifact|enchantment)"
      + @"|target creature gets -"
      + @"|damage to (target creature|any target|each creature)",
        RegexOptions.Compiled);

    private static bool IsRamp(string t) =>
        RampPattern.IsMatch(t) || LandRampPattern.IsMatch(t)
        || t.Contains("play an additional land", StringComparison.Ordinal);

    private static bool IsDraw(string t) =>
        DrawPattern.IsMatch(t) || WheelPattern.IsMatch(t);

    private static bool IsRemoval(string t) => RemovalPattern.IsMatch(t);
}
