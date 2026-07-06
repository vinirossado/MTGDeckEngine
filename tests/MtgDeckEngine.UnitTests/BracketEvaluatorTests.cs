using MtgDeckEngine.Core.Brackets;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class BracketEvaluatorTests
{
    [Fact]
    public void Vanilla_budget_list_is_bracket_2()
    {
        var deck = new[] { "Llanowar Elves", "Cultivate", "Forest", "Island", "Mountain" };
        var b = BracketEvaluator.Evaluate(deck);

        Assert.Equal(2, b.Level);
        Assert.Equal("Core", b.Label);
        Assert.Equal(0, b.GameChangerCount);
        Assert.False(b.HasMassLandDenial);
        Assert.True(b.IsEstimate);
    }

    [Fact]
    public void A_single_game_changer_forces_at_least_bracket_3()
    {
        var deck = new[] { "Rhystic Study", "Llanowar Elves", "Forest" };
        var b = BracketEvaluator.Evaluate(deck);

        Assert.True(b.Level >= 3);
        Assert.Equal(1, b.GameChangerCount);
        Assert.Contains("Rhystic Study", b.GameChangersFound);
    }

    [Fact]
    public void More_than_three_game_changers_forces_at_least_bracket_4()
    {
        var deck = new[] { "Rhystic Study", "Cyclonic Rift", "Mana Vault", "Demonic Tutor", "The One Ring" };
        var b = BracketEvaluator.Evaluate(deck);

        Assert.True(b.Level >= 4);
        Assert.Equal(5, b.GameChangerCount);
    }

    [Fact]
    public void Mass_land_denial_forces_at_least_bracket_4()
    {
        var deck = new[] { "Armageddon", "Llanowar Elves", "Plains" };
        var b = BracketEvaluator.Evaluate(deck);

        Assert.True(b.Level >= 4);
        Assert.True(b.HasMassLandDenial);
    }

    [Fact]
    public void Matches_are_case_insensitive_and_handle_split_cards()
    {
        var deck = new[] { "rhystic study", "Boom // Bust" };
        var b = BracketEvaluator.Evaluate(deck);

        Assert.Equal(1, b.GameChangerCount);   // rhystic study
        Assert.True(b.HasMassLandDenial);      // Boom // Bust
    }
}
