using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class CommanderDiscoveryTests
{
    /// <summary>
    /// Build one tournament deck: a commander, a price, a Game Changer count and
    /// a match record. Mirrors what TopDeck ingestion writes.
    /// </summary>
    private static void AddDeck(
        RdfGraph g, string commanderSlug, string commanderName, string deckId,
        decimal? priceEur, int gameChangers, int wins, int losses, int placement = 20)
    {
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri("test", deckId)));
        var commander = g.CreateUriNode(new Uri(MtgVocab.CommanderUri(commanderSlug)));
        var entry = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri("test", "T1", deckId)));

        void P(INode s, string prop, INode o)
            => g.Assert(s, g.CreateUriNode(new Uri(MtgVocab.Property(prop))), o);
        ILiteralNode I(int i) => g.CreateLiteralNode(
            i.ToString(), new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger));
        ILiteralNode D(decimal d) => g.CreateLiteralNode(
            RdfLiterals.Decimal(d), new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

        P(deck, "hasCommander", commander);
        P(deck, "hasGameChangerCount", I(gameChangers));
        if (priceEur is decimal p) P(deck, "hasTotalPriceEur", D(p));
        P(commander, "hasName", g.CreateLiteralNode(commanderName));

        P(entry, "hasDeck", deck);
        P(entry, "hasPlacement", I(placement));
        P(entry, "hasWinsSwiss", I(wins));
        P(entry, "hasLossesSwiss", I(losses));
    }

    private static async Task<CommanderDiscoveryService> SeedAsync(Action<RdfGraph> seed)
    {
        var repo = new InMemoryGraphRepository();
        var g = new RdfGraph();
        seed(g);
        await repo.WriteAsync(g, null, default);
        return new CommanderDiscoveryService(repo);
    }

    [Theory]
    [InlineData(0, 2)]   // no Game Changers -> Core
    [InlineData(1, 3)]
    [InlineData(3, 3)]   // up to three -> Upgraded
    [InlineData(4, 4)]   // more than three -> Optimized
    [InlineData(9, 4)]
    public void Bracket_floor_follows_the_game_changer_count(int gc, int expected)
        => Assert.Equal(expected, CommanderDiscoveryService.BracketFloorFor(gc));

    [Fact]
    public async Task Bracket_cap_excludes_commanders_whose_decks_exceed_it()
    {
        var svc = await SeedAsync(g =>
        {
            for (var i = 0; i < 4; i++)
                AddDeck(g, "casual-cmd", "Casual Commander", $"casual{i}", 100m, 0, 5, 5);
            for (var i = 0; i < 4; i++)
                AddDeck(g, "cedh-cmd", "cEDH Commander", $"cedh{i}", 100m, 8, 9, 1);
        });

        var b2 = await svc.FindCommandersAsync(new CommanderDiscoveryFilter(MaxBracket: 2));
        Assert.Equal(["casual-cmd"], b2.Select(p => p.CommanderSlug));

        // Uncapped, the cEDH commander wins on record.
        var any = await svc.FindCommandersAsync(new CommanderDiscoveryFilter());
        Assert.Equal("cedh-cmd", any[0].CommanderSlug);
    }

    [Fact]
    public async Task Budget_filter_matches_on_the_cheapest_recorded_deck()
    {
        var svc = await SeedAsync(g =>
        {
            // Expensive commander: no build under EUR 500.
            for (var i = 0; i < 3; i++)
                AddDeck(g, "pricey", "Pricey", $"p{i}", 800m, 0, 6, 4);
            // Budget commander: one cheap build among pricier ones.
            AddDeck(g, "thrifty", "Thrifty", "t0", 90m, 0, 6, 4);
            AddDeck(g, "thrifty", "Thrifty", "t1", 700m, 0, 6, 4);
            AddDeck(g, "thrifty", "Thrifty", "t2", 750m, 0, 6, 4);
        });

        var picks = await svc.FindCommandersAsync(
            new CommanderDiscoveryFilter(MaxBudgetEur: 100m, MinDeckCount: 1));

        var pick = Assert.Single(picks);
        Assert.Equal("thrifty", pick.CommanderSlug);
        Assert.Equal(90m, pick.MinDeckPriceEur);
    }

    [Fact]
    public async Task A_thin_sample_does_not_outrank_a_proven_commander()
    {
        // One undefeated deck is not evidence. Shrinking toward the pool mean
        // fails this — an above-average rate stays above average at any sample
        // size — which is why the ranking uses a Wilson lower bound instead.
        var svc = await SeedAsync(g =>
        {
            AddDeck(g, "lucky", "Lucky One-Off", "l0", 100m, 0, 3, 0);
            for (var i = 0; i < 30; i++)
                AddDeck(g, "proven", "Proven Staple", $"pr{i}", 100m, 0, 7, 3);
        });

        var picks = await svc.FindCommandersAsync(new CommanderDiscoveryFilter(MinDeckCount: 1));

        Assert.Equal("proven", picks[0].CommanderSlug);
        var lucky = picks.Single(p => p.CommanderSlug == "lucky");
        // Raw rate favours the one-off; the ranking figure does not.
        Assert.Equal(1.0m, lucky.WinRate);
        Assert.True(lucky.AdjustedWinRate < picks[0].AdjustedWinRate,
            $"lucky {lucky.AdjustedWinRate} should rank below proven {picks[0].AdjustedWinRate}");
    }

    [Theory]
    // A perfect record on a tiny sample scores far below its face value.
    [InlineData(3, 3, 0.30, 0.55)]
    // A long, merely-good record lands close to it.
    [InlineData(210, 300, 0.60, 0.69)]
    // No games at all is not a 100% win rate.
    [InlineData(0, 0, 0.0, 0.0)]
    public void Wilson_bound_discounts_thin_samples(int wins, int games, double lo, double hi)
    {
        var b = (double)CommanderDiscoveryService.WilsonLowerBound(wins, games);
        Assert.InRange(b, lo, hi);
    }

    [Fact]
    public async Task Commanders_below_the_deck_floor_are_dropped()
    {
        var svc = await SeedAsync(g =>
        {
            AddDeck(g, "oneoff", "One Off", "o0", 50m, 0, 9, 0);
            for (var i = 0; i < 5; i++)
                AddDeck(g, "regular", "Regular", $"r{i}", 50m, 0, 5, 5);
        });

        var picks = await svc.FindCommandersAsync(new CommanderDiscoveryFilter(MinDeckCount: 3));

        Assert.DoesNotContain(picks, p => p.CommanderSlug == "oneoff");
        Assert.Contains(picks, p => p.CommanderSlug == "regular");
    }

    [Fact]
    public async Task Reports_the_record_and_bracket_it_ranked_on()
    {
        var svc = await SeedAsync(g =>
        {
            for (var i = 0; i < 3; i++)
                AddDeck(g, "cmd", "Some Commander", $"d{i}", 120m, 2, 6, 4, placement: 4);
        });

        var pick = Assert.Single(await svc.FindCommandersAsync(new CommanderDiscoveryFilter()));
        Assert.Equal(3, pick.DeckCount);
        Assert.Equal(18, pick.TournamentWins);
        Assert.Equal(12, pick.TournamentLosses);
        Assert.Equal(0.6m, pick.WinRate);
        Assert.Equal(3, pick.TopCutCount);
        Assert.Equal(2, pick.MaxGameChangers);
        Assert.Equal(3, pick.EstimatedBracket);
    }
}
