using MtgDeckEngine.Core.Brackets;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class DeckThemeTests
{
    [Theory]
    // Real wheel text — the archetype the request that prompted this was about.
    [InlineData("wheel", "Each player discards their hand, then draws seven cards.")]
    [InlineData("wheel",
        "Each player shuffles their hand and graveyard into their library, then draws seven cards.")]
    [InlineData("wheel",
        "Each player discards their hand, then draws cards equal to the greatest number of cards a player discarded this way.")]
    // Drain.
    [InlineData("lifedrain", "Each opponent loses 2 life and you gain 2 life.")]
    [InlineData("lifedrain", "Target opponent loses 3 life.")]
    // Others, one apiece.
    [InlineData("tokens", "Create two 1/1 white Soldier creature tokens.")]
    [InlineData("storm", "Storm (When you cast this spell, copy it for each spell cast before it this turn.)")]
    [InlineData("stax", "Whenever an opponent casts a spell, counter it unless that player pays {2}.")]
    [InlineData("sacrifice", "Sacrifice a creature: Add one mana of any color.")]
    [InlineData("counters", "Put a +1/+1 counter on target creature.")]
    [InlineData("graveyard", "Return target creature card from your graveyard to the battlefield.")]
    public void Matches_the_theme_it_should(string key, string oracleText)
    {
        var theme = DeckTheme.All.Single(t => t.Key == key);
        Assert.True(theme.Matches(oracleText), $"{key} did not match: {oracleText}");
    }

    [Fact]
    public void A_plain_creature_matches_nothing()
    {
        const string vanilla = "Flying, vigilance.";
        Assert.All(DeckTheme.All, t => Assert.False(t.Matches(vanilla), $"{t.Key} matched a vanilla creature"));
    }

    [Fact]
    public void Missing_oracle_text_never_matches()
    {
        // ~9% of cards have no text in the graph. They must not be swept into
        // every theme by accident.
        Assert.All(DeckTheme.All, t =>
        {
            Assert.False(t.Matches(null));
            Assert.False(t.Matches(""));
        });
    }

    [Fact]
    public void Resolve_ignores_unknown_keys_rather_than_throwing()
    {
        var resolved = DeckTheme.Resolve(["wheel", "not-a-theme", "LIFEDRAIN"]);

        Assert.Equal(["wheel", "lifedrain"], resolved.Select(t => t.Key));
    }

    [Fact]
    public void Resolve_of_nothing_is_empty()
    {
        Assert.Empty(DeckTheme.Resolve(null));
        Assert.Empty(DeckTheme.Resolve([]));
    }

    [Fact]
    public void Theme_keys_are_unique_and_lowercase()
    {
        // They arrive as a comma-separated query parameter, so a duplicate or a
        // stray capital would silently resolve to the wrong thing.
        var keys = DeckTheme.All.Select(t => t.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Equal(k.ToLowerInvariant(), k));
    }
}
