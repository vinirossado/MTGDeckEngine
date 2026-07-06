using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Brackets;

/// <summary>
/// Estimates a deck's Commander Bracket (1–5) from card names alone, using the
/// deterministic rules of WotC's bracket system that we can evaluate without a
/// combo database: the Game Changers list, mass land denial, and extra-turn
/// spells.
///
/// LIMITATION: two-card infinite combos (a real Bracket-4 trigger) are NOT
/// detected — that needs a combo DB such as Commander Spellbook. The result is
/// therefore a floor/estimate, surfaced as <see cref="DeckBracket.IsEstimate"/>.
///
/// The name lists below are curated and must be refreshed when WotC updates the
/// official lists (Game Changers currently ~53 cards).
/// </summary>
public static class BracketEvaluator
{
    // Official Game Changers list (curated). Banned in brackets 1–2, limited to
    // 3 in bracket 3, unlimited in 4–5. Keep in sync with WotC's published list.
    public static readonly IReadOnlySet<string> GameChangers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // White
        "Drannith Magistrate", "Enlightened Tutor", "Serra's Sanctum", "Smothering Tithe", "Trouble in Pairs",
        // Blue
        "Cyclonic Rift", "Expropriate", "Fierce Guardianship", "Force of Will", "Intuition",
        "Mystical Tutor", "Rhystic Study", "Thassa's Oracle", "Urza, Lord High Artificer",
        // Black
        "Ad Nauseam", "Bolas's Citadel", "Demonic Tutor", "Imperial Seal", "Necropotence",
        "Opposition Agent", "Tergrid, God of Fright", "Vampiric Tutor",
        // Red
        "Jeska's Will", "Underworld Breach", "Gamble",
        // Green
        "Food Chain", "Gaea's Cradle", "Survival of the Fittest", "Vorinclex, Voice of Hunger",
        // Multicolour
        "Aura Shards", "Coalition Victory", "Grand Arbiter Augustin IV", "Kinnan, Bonder Prodigy",
        "Notion Thief", "Winota, Joiner of Forces", "Yuriko, the Tiger's Shadow",
        // Colourless / artifact / land
        "Ancient Tomb", "Chrome Mox", "Glacial Chasm", "Grim Monolith", "Lion's Eye Diamond",
        "Mana Vault", "Mishra's Workshop", "Mox Diamond", "Panoptic Mirror", "The One Ring",
        "The Tabernacle at Pendrell Vale", "Trinisphere",
    };

    // Mass land denial — presence forces at least Bracket 4.
    public static readonly IReadOnlySet<string> MassLandDenial = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Armageddon", "Ravages of War", "Catastrophe", "Cataclysm", "Jokulhaups", "Obliterate",
        "Decree of Annihilation", "Boom // Bust", "Impending Disaster", "Devastation",
        "Fall of the Thran", "Sunder", "Wildfire", "Burning of Xinye",
    };

    // Extra-turn spells — a soft signal that pushes a deck up a bracket.
    public static readonly IReadOnlySet<string> ExtraTurnSpells = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Time Warp", "Temporal Manipulation", "Capture of Jingzhou", "Temporal Mastery",
        "Nexus of Fate", "Time Stretch", "Karn's Temporal Sundering", "Alrund's Epiphany",
        "Time Walk", "Timestream Navigator", "Expropriate",
    };

    private static readonly string[] Labels =
        { "", "Exhibition", "Core", "Upgraded", "Optimized", "cEDH" };

    /// <summary>Normalise a card name for matching: trim and take the front face
    /// of split/MDFC cards (except where the full "A // B" name is itself listed).</summary>
    private static string FrontFace(string name)
    {
        var trimmed = name.Trim();
        var idx = trimmed.IndexOf(" // ", StringComparison.Ordinal);
        return idx > 0 ? trimmed[..idx] : trimmed;
    }

    public static DeckBracket Evaluate(IEnumerable<string> cardNames)
    {
        var gameChangers = new List<string>();
        var hasMld = false;
        var extraTurns = 0;

        foreach (var raw in cardNames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();
            var front = FrontFace(name);

            // Match either the full printed name (handles "Boom // Bust") or the front face.
            if (GameChangers.Contains(name) || GameChangers.Contains(front))
                gameChangers.Add(name);
            if (MassLandDenial.Contains(name) || MassLandDenial.Contains(front))
                hasMld = true;
            if (ExtraTurnSpells.Contains(name) || ExtraTurnSpells.Contains(front))
                extraTurns++;
        }

        var reasons = new List<string>();
        var level = 2; // Baseline "Core"; we can't detect the ultra-casual Bracket 1.

        var gcCount = gameChangers.Count;
        if (gcCount > 0)
        {
            var floor = gcCount > 3 ? 4 : 3;
            level = Math.Max(level, floor);
            reasons.Add(gcCount > 3
                ? $"{gcCount} Game Changers (>3 forces Bracket 4+): {string.Join(", ", gameChangers)}"
                : $"{gcCount} Game Changer(s) (Bracket 3+): {string.Join(", ", gameChangers)}");
        }

        if (hasMld)
        {
            level = Math.Max(level, 4);
            reasons.Add("Mass land denial present — forces Bracket 4 minimum.");
        }

        if (extraTurns > 0)
        {
            // A lone extra-turn spell nudges to Upgraded; several suggest a turns
            // chain, which belongs in Optimized.
            level = Math.Max(level, extraTurns >= 3 ? 4 : 3);
            reasons.Add($"{extraTurns} extra-turn spell(s) detected.");
        }

        if (reasons.Count == 0)
            reasons.Add("No Game Changers, mass land denial, or extra-turn spells detected.");

        reasons.Add("Estimate only: two-card infinite combos are not detected (no combo database).");

        level = Math.Clamp(level, 1, 5);
        return new DeckBracket(
            Level:             level,
            Label:             Labels[level],
            GameChangerCount:  gcCount,
            GameChangersFound: gameChangers,
            HasMassLandDenial: hasMld,
            HasExtraTurns:     extraTurns > 0,
            Reasons:           reasons);
    }
}
