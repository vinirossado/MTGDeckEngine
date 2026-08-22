using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Brackets;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Mappers;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

/// <summary>
/// Covers the three behaviours that decide whether a "build me a deck for €X at
/// bracket Y" answer is trustworthy: cards without a price must not be spendable,
/// tournament match records must drive ranking, and the bracket cap must bind.
/// </summary>
public class BudgetBracketAndWinRateTests
{
    private const string Slug = "xyris-the-writhing-storm";
    private static readonly string[] Identity = { "R", "G", "U" };

    private sealed record Seed(
        string OracleId,
        string Name,
        decimal? Price,
        decimal Inclusion,
        string TypeLine = "Creature — Spirit",
        int Wins = 0,
        int Losses = 0);

    private static async Task<InMemoryGraphRepository> SeedAsync(IEnumerable<Seed> seeds)
    {
        var repo = new InMemoryGraphRepository();
        var global = new RdfGraph();
        var entries = new List<EdhrecCardEntry>();
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = seeds.ToList();

        foreach (var s in list)
        {
            ScryfallToRdfMapper.AssertCard(global, new CardDto(
                s.OracleId, s.Name, Identity, Identity, s.TypeLine,
                null, s.Price, null, true));
            entries.Add(new EdhrecCardEntry(s.Name, Slug, "creatures", s.Inclusion, 1m, 100, 1000)
            {
                CategoryLabel = s.TypeLine.Contains("Land") ? "Lands" : "Creatures",
            });
            nameToId[s.Name] = s.OracleId;
        }
        await repo.WriteAsync(global, null, default);

        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(Slug)), default);

        // One tournament deck per card carrying that card's match record, so the
        // win-rate subquery has something per-card to aggregate.
        var tourney = new RdfGraph();
        var i = 0;
        foreach (var s in list.Where(s => s.Wins + s.Losses > 0))
        {
            EdhTop16ToRdfMapper.AssertEntry(tourney, Slug, new Ingestion.Dto.EdhTop16Entry
            {
                Id = $"entry-{i}",
                Standing = 1,
                WinsSwiss = s.Wins,
                LossesSwiss = s.Losses,
                WinsBracket = 0,
                LossesBracket = 0,
                Maindeck = [new Ingestion.Dto.EdhTop16Card { Name = s.Name, OracleId = s.OracleId }],
                Tournament = new Ingestion.Dto.EdhTop16Tournament
                {
                    TID = $"T-{i}", Name = $"Event {i}",
                    TournamentDate = "2026-01-01", Size = 64, TopCut = 16, SwissRounds = 5,
                },
            });
            i++;
        }
        await repo.WriteAsync(tourney, null, default);
        return repo;
    }

    private static RecommendationFilter Filter() => new(ExcludeBasicLands: false, Limit: 300);

    [Fact]
    public async Task Cards_without_a_price_are_never_drafted()
    {
        // "Priceless Bomb" has no price triple at all. Under the old
        // `PriceEur ?? 0m` behaviour it looked free and would always be taken.
        var seeds = Enumerable.Range(0, 80)
            .Select(i => new Seed($"fill-{i:D2}", $"Filler {i:D2}", 0.10m, 1m))
            .Append(new Seed("bomb", "Priceless Bomb", null, 99m))
            .ToList();

        var svc = new DeckRecommendationService(await SeedAsync(seeds));
        var deck = await svc.BuildBudgetDeckAsync(Slug, 50m, Filter(), null, default);

        Assert.DoesNotContain(deck.Cards, c => c.Name == "Priceless Bomb");
        Assert.All(deck.Cards, c => Assert.NotNull(c.PriceEur));
    }

    [Fact]
    public async Task Reported_total_never_exceeds_the_budget()
    {
        var seeds = Enumerable.Range(0, 80)
            .Select(i => new Seed($"fill-{i:D2}", $"Filler {i:D2}", 0.10m, 1m))
            .Concat(Enumerable.Range(0, 20)
                .Select(i => new Seed($"stap-{i:D2}", $"Staple {i:D2}", 7m, 90m)))
            .ToList();

        var svc = new DeckRecommendationService(await SeedAsync(seeds));
        var deck = await svc.BuildBudgetDeckAsync(Slug, 25m, Filter(), null, default);

        Assert.True(deck.TotalPriceEur <= 25m,
            $"deck cost {deck.TotalPriceEur} exceeded the 25 budget");
        Assert.Equal(deck.TotalPriceEur, deck.Cards.Sum(c => c.PriceEur ?? 0m));
    }

    [Fact]
    public async Task A_winning_card_outranks_an_equally_popular_losing_one()
    {
        // Same price, same inclusion — only the match record differs. The winner
        // should be drafted and the loser left out.
        var seeds = Enumerable.Range(0, 80)
            .Select(i => new Seed($"fill-{i:D2}", $"Filler {i:D2}", 0.10m, 1m))
            .Append(new Seed("winner", "Winner Card", 5m, 50m, Wins: 40, Losses: 5))
            .Append(new Seed("loser",  "Loser Card",  5m, 50m, Wins: 5,  Losses: 40))
            .ToList();

        var svc = new DeckRecommendationService(await SeedAsync(seeds));
        // Budget fits the fillers plus exactly one of the two 5-euro cards.
        var deck = await svc.BuildBudgetDeckAsync(Slug, 14m, Filter(), null, default);

        Assert.Contains(deck.Cards, c => c.Name == "Winner Card");
        Assert.DoesNotContain(deck.Cards, c => c.Name == "Loser Card");
    }

    [Fact]
    public async Task Win_rate_is_projected_from_the_graph()
    {
        var seeds = new[] { new Seed("winner", "Winner Card", 5m, 50m, Wins: 12, Losses: 4) };
        var svc = new DeckRecommendationService(await SeedAsync(seeds));

        var recs = await svc.GetRecommendationsAsync(
            Slug, new RecommendationFilter(Source: RecommendationSource.All, Limit: 50), default);

        var card = Assert.Single(recs, c => c.Name == "Winner Card");
        Assert.Equal(12, card.TournamentWins);
        Assert.Equal(4, card.TournamentLosses);
        Assert.Equal(0.75m, card.WinRate);
    }

    [Fact]
    public async Task Bracket_cap_keeps_game_changers_out_of_a_bracket_2_build()
    {
        // Rhystic Study and Cyclonic Rift are on the Game Changers list, so a
        // Bracket 2 deck may contain none of them however well they score.
        var seeds = Enumerable.Range(0, 80)
            .Select(i => new Seed($"fill-{i:D2}", $"Filler {i:D2}", 0.10m, 1m))
            .Append(new Seed("gc1", "Rhystic Study", 1m, 99m, "Enchantment", Wins: 90, Losses: 1))
            .Append(new Seed("gc2", "Cyclonic Rift", 1m, 99m, "Instant", Wins: 90, Losses: 1))
            .ToList();

        var svc = new DeckRecommendationService(await SeedAsync(seeds));

        var capped = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: 2, default);
        Assert.DoesNotContain(capped.Cards, c => c.Name is "Rhystic Study" or "Cyclonic Rift");

        // Without the cap the same build happily takes them — proving the
        // exclusion above comes from the constraint, not from scoring.
        var uncapped = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: null, default);
        Assert.Contains(uncapped.Cards, c => c.Name is "Rhystic Study" or "Cyclonic Rift");
    }

    [Fact]
    public void Bracket_3_allows_at_most_three_game_changers()
    {
        var c = BracketConstraint.For(3);
        Assert.Equal(3, c.MaxGameChangers);
        Assert.False(c.AllowMassLandDenial);
        Assert.True(c.IsGameChanger("Rhystic Study"));
        Assert.False(c.IsBanned("Rhystic Study"));   // allowed, but counted
        Assert.True(c.IsBanned("Armageddon"));       // mass land denial
    }

    [Fact]
    public void Bracket_4_and_above_are_unconstrained()
    {
        Assert.True(BracketConstraint.For(4).IsUnconstrained);
        Assert.True(BracketConstraint.For(5).IsUnconstrained);
        Assert.False(BracketConstraint.For(3).IsUnconstrained);
    }
}
