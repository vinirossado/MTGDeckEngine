using MtgDeckEngine.Core.Brackets;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class ComboBreakerTests
{
    private static IReadOnlyList<string> Cut(
        IReadOnlyList<IReadOnlyList<string>> combos,
        IReadOnlyDictionary<string, double>? scores = null,
        ISet<string>? unremovable = null)
        => ComboBreaker.ChooseCardsToCut(
            combos,
            isRemovable: n => unremovable?.Contains(n) != true,
            scoreOf:     n => scores is not null && scores.TryGetValue(n, out var s) ? s : 1.0);

    [Fact]
    public void One_card_shared_by_two_combos_is_cut_once()
    {
        // The whole point. Cutting each combo's weakest half independently
        // removes two cards here; Dualcaster is in both, so one cut suffices.
        var cut = Cut([
            ["Dualcaster Mage", "Blur"],
            ["Dualcaster Mage", "Ghostly Flicker"],
        ]);

        Assert.Equal(["Dualcaster Mage"], cut);
    }

    [Fact]
    public void A_hub_across_three_combos_is_still_a_single_cut()
    {
        var cut = Cut([
            ["Thassa's Oracle", "Demonic Consultation"],
            ["Thassa's Oracle", "Tainted Pact"],
            ["Thassa's Oracle", "Hermit Druid"],
        ]);

        Assert.Equal(["Thassa's Oracle"], cut);
    }

    [Fact]
    public void Disjoint_combos_each_need_their_own_cut()
    {
        var cut = Cut([
            ["A1", "A2"],
            ["B1", "B2"],
        ]);

        Assert.Equal(2, cut.Count);
        Assert.Contains(cut, c => c is "A1" or "A2");
        Assert.Contains(cut, c => c is "B1" or "B2");
    }

    [Fact]
    public void Among_equally_small_covers_the_cheapest_wins()
    {
        // Both halves break the combo; the one worth less should go.
        var cut = Cut(
            [["Expensive Bomb", "Cheap Filler"]],
            scores: new Dictionary<string, double>
            {
                ["Expensive Bomb"] = 0.9,
                ["Cheap Filler"]   = 0.1,
            });

        Assert.Equal(["Cheap Filler"], cut);
    }

    [Fact]
    public void A_smaller_cover_beats_a_cheaper_but_larger_one()
    {
        // Cutting the shared hub costs more score than cutting both leaves, but
        // it is one card instead of two — fewer cards lost is the objective.
        var cut = Cut(
            [
                ["Hub", "Leaf A"],
                ["Hub", "Leaf B"],
            ],
            scores: new Dictionary<string, double>
            {
                ["Hub"]    = 0.9,
                ["Leaf A"] = 0.1,
                ["Leaf B"] = 0.1,
            });

        Assert.Equal(["Hub"], cut);
    }

    [Fact]
    public void Combos_reachable_only_through_the_commander_are_skipped()
    {
        // The commander is not one of the 99, so that combo cannot be broken.
        // It must not drag an unrelated card out with it.
        var cut = Cut(
            [
                ["Kefka, Court Mage", "Psychosis Crawler"],
                ["Dualcaster Mage", "Blur"],
            ],
            unremovable: new HashSet<string> { "Kefka, Court Mage", "Psychosis Crawler" });

        Assert.Single(cut);
        Assert.Contains(cut[0], new[] { "Dualcaster Mage", "Blur" });
    }

    [Fact]
    public void Nothing_removable_means_nothing_is_cut()
    {
        var cut = Cut(
            [["Kefka, Court Mage", "Psychosis Crawler"]],
            unremovable: new HashSet<string> { "Kefka, Court Mage", "Psychosis Crawler" });

        Assert.Empty(cut);
    }

    [Fact]
    public void No_combos_means_no_cuts()
        => Assert.Empty(Cut([]));

    [Fact]
    public void Duplicate_combos_count_as_one_constraint()
    {
        var cut = Cut([
            ["A", "B"],
            ["B", "A"],
        ]);

        Assert.Single(cut);
    }

    [Fact]
    public void Large_inputs_still_return_a_valid_cover()
    {
        // Past the exhaustive limit it falls back to greedy — which must still
        // hit every combo, just not necessarily minimally.
        var combos = Enumerable.Range(0, 40)
            .Select(i => (IReadOnlyList<string>)new[] { $"X{i}", $"Y{i}" })
            .ToList();

        var cut = Cut(combos);
        var set = new HashSet<string>(cut, StringComparer.OrdinalIgnoreCase);

        Assert.All(combos, c => Assert.True(c.Any(set.Contains), $"combo {c[0]}/{c[1]} uncovered"));
    }
}
