namespace MtgDeckEngine.Core.Brackets;

/// <summary>
/// Chooses which cards to cut so that every detected two-card combo loses at
/// least one participant — a minimum hitting set over the combos.
///
/// Cutting each combo's weakest participant independently is the obvious
/// approach and it over-cuts: when two combos share a card, removing that one
/// card breaks both, but the independent pass removes two. Every extra cut is a
/// card the budget paid for and the scorer wanted.
/// </summary>
public static class ComboBreaker
{
    /// <summary>
    /// Above this many distinct removable participants the exhaustive search is
    /// abandoned for the greedy one. Real decks produce a handful; the guard is
    /// there so a pathological list cannot hang a request.
    /// </summary>
    private const int ExhaustiveLimit = 22;

    /// <summary>
    /// Cards to remove, smallest set first and cheapest in lost score among the
    /// sets of that size.
    /// </summary>
    /// <param name="combos">Participant names per detected combo.</param>
    /// <param name="isRemovable">
    /// Whether a name can actually be cut. A combo running through the commander
    /// has an un-removable participant, and one with no removable participant at
    /// all is unfixable — those are skipped rather than forcing a pointless cut.
    /// </param>
    /// <param name="scoreOf">Card score; lower is cheaper to lose.</param>
    public static IReadOnlyList<string> ChooseCardsToCut(
        IReadOnlyList<IReadOnlyList<string>> combos,
        Func<string, bool> isRemovable,
        Func<string, double> scoreOf)
    {
        // Reduce to the combos we can actually break, keeping only removable
        // participants. Duplicate combos collapse — they are one constraint.
        var constraints = combos
            .Select(c => c.Where(isRemovable)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToArray())
            .Where(c => c.Length > 0)
            .Distinct(new SetComparer())
            .ToList();
        if (constraints.Count == 0) return [];

        var candidates = constraints
            .SelectMany(c => c)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scoreOf)                       // cheapest first
            .ToList();

        if (candidates.Count > ExhaustiveLimit)
            return GreedyCover(constraints, candidates, scoreOf);

        // Exact: the smallest k that hits every constraint, and among those the
        // combination with the least score given up.
        for (var k = 1; k <= candidates.Count; k++)
        {
            IReadOnlyList<string>? best = null;
            var bestScore = double.MaxValue;

            foreach (var combo in Combinations(candidates, k))
            {
                var set = new HashSet<string>(combo, StringComparer.OrdinalIgnoreCase);
                if (!constraints.All(c => c.Any(set.Contains))) continue;

                var lost = combo.Sum(scoreOf);
                if (lost < bestScore) { bestScore = lost; best = combo; }
            }

            if (best is not null) return best;
        }

        return candidates;   // unreachable: cutting everything always covers
    }

    /// <summary>
    /// Fallback for pathologically large inputs: repeatedly take the card that
    /// covers the most still-uncovered combos, cheapest first on ties.
    /// </summary>
    private static IReadOnlyList<string> GreedyCover(
        List<string[]> constraints, List<string> candidates, Func<string, double> scoreOf)
    {
        var uncovered = constraints.ToList();
        var chosen = new List<string>();

        while (uncovered.Count > 0)
        {
            var pick = candidates
                .Where(c => !chosen.Contains(c, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(c => uncovered.Count(u => u.Contains(c, StringComparer.OrdinalIgnoreCase)))
                .ThenBy(scoreOf)
                .First();
            chosen.Add(pick);
            uncovered.RemoveAll(u => u.Contains(pick, StringComparer.OrdinalIgnoreCase));
        }
        return chosen;
    }

    private static IEnumerable<IReadOnlyList<string>> Combinations(List<string> items, int k)
    {
        var idx = new int[k];
        for (var i = 0; i < k; i++) idx[i] = i;

        while (true)
        {
            yield return idx.Select(i => items[i]).ToArray();

            var pos = k - 1;
            while (pos >= 0 && idx[pos] == items.Count - k + pos) pos--;
            if (pos < 0) yield break;
            idx[pos]++;
            for (var i = pos + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
        }
    }

    /// <summary>Treats two constraints as equal when they hold the same names.</summary>
    private sealed class SetComparer : IEqualityComparer<string[]>
    {
        public bool Equals(string[]? a, string[]? b)
            => a is not null && b is not null
            && a.Length == b.Length
            && a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);

        public int GetHashCode(string[] a)
            => a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Aggregate(17, (h, x) => h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(x));
    }
}
