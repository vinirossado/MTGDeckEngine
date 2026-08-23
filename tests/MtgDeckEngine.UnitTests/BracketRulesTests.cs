using MtgDeckEngine.Core.Brackets;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class BracketRulesTests
{
    private static BracketRules.Signals S(
        int gc = 0, bool mld = false, bool extraTurns = false,
        int earlyCombos = 0, int lateCombos = 0, bool banned = false)
        => new(gc, mld, extraTurns, earlyCombos, lateCombos, banned);

    [Fact]
    public void Four_game_changers_is_bracket_four_not_cedh()
    {
        // The reported case: a EUR 600 Xyris build with 4 Game Changers, no
        // combos and no mass land denial came back as cEDH. WotC puts "more
        // than 3 Game Changers" at Bracket 4.
        var (level, label) = BracketRules.Evaluate(S(gc: 4));

        Assert.Equal(4, level);
        Assert.Equal("Optimized", label);
    }

    [Theory]
    [InlineData(0, 2)]   // nothing flagged -> Core
    [InlineData(1, 3)]
    [InlineData(3, 3)]   // three is the Bracket 3 allowance
    [InlineData(4, 4)]   // more than three requires Bracket 4
    [InlineData(9, 4)]
    public void Game_changer_count_sets_the_bracket(int gc, int expected)
        => Assert.Equal(expected, BracketRules.Evaluate(S(gc: gc)).Level);

    [Fact]
    public void Mass_land_denial_forces_bracket_four()
        => Assert.Equal(4, BracketRules.Evaluate(S(mld: true)).Level);

    [Fact]
    public void An_early_combo_forces_bracket_four()
        => Assert.Equal(4, BracketRules.Evaluate(S(earlyCombos: 1)).Level);

    [Fact]
    public void A_late_combo_is_allowed_at_bracket_three()
    {
        // Bracket 3 permits combos that only assemble late; it is the early
        // ones it rules out.
        Assert.Equal(3, BracketRules.Evaluate(S(lateCombos: 2)).Level);
    }

    [Fact]
    public void Banned_cards_are_not_a_bracket()
    {
        var (level, label) = BracketRules.Evaluate(S(banned: true, gc: 9));
        Assert.Equal(0, level);
        Assert.Contains("Illegal", label);
    }

    [Fact]
    public void Nothing_derivable_can_ever_reach_bracket_five()
    {
        // Brackets 4 and 5 share their card-list rules; the difference is
        // mindset. Claiming 5 from a decklist is claiming to read intent.
        var extremes = new[]
        {
            S(gc: 50, mld: true, extraTurns: true, earlyCombos: 20, lateCombos: 20),
            S(gc: 4),
            S(earlyCombos: 1),
            S(mld: true),
        };

        Assert.All(extremes, s =>
            Assert.True(BracketRules.Evaluate(s).Level <= BracketRules.MaxDerivable,
                "a decklist cannot establish Bracket 5"));
    }

    [Fact]
    public void Bracket_one_is_not_claimed_either()
    {
        // Exhibition and Core differ by theme and intent, not by any rule a
        // list breaks — so 2 is the floor rather than 1.
        Assert.Equal(BracketRules.Floor, BracketRules.Evaluate(S()).Level);
        Assert.Equal(2, BracketRules.Floor);
    }
}
