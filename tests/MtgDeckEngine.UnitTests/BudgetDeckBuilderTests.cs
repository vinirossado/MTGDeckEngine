using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Mappers;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class BudgetDeckBuilderTests
{
    private const string Slug = "xyris-the-writhing-storm";
    private static readonly string[] Identity = { "R", "G", "U" };
    private static readonly HashSet<string> BasicNames =
        new(StringComparer.Ordinal) { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    // Seed a pool of cheap "filler" creatures plus a set of expensive high-inclusion
    // "staples" (same role), all in a Temur colour identity. The builder should
    // always complete to 99 with basics and spend the budget upgrading fillers to
    // staples.
    private static async Task<DeckRecommendationService> SeedAsync(
        int fillers = 70, int staples = 20, decimal staplePrice = 8m)
    {
        var repo = new InMemoryGraphRepository();
        var global = new RdfGraph();
        var entries = new List<EdhrecCardEntry>();
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string oid, string name, decimal price, decimal inclusion)
        {
            ScryfallToRdfMapper.AssertCard(global, new CardDto(
                oid, name, Identity, Identity, "Creature — Spirit",
                null, price, null, true));
            entries.Add(new EdhrecCardEntry(name, Slug, "creatures", inclusion, 1m, 100, 1000)
            {
                CategoryLabel = "Creatures",
            });
            nameToId[name] = oid;
        }

        for (var i = 0; i < fillers; i++)
            Add($"fill-{i:D2}", $"Filler {i:D2}", 0.15m, 1m);
        for (var i = 0; i < staples; i++)
            Add($"stap-{i:D2}", $"Staple {i:D2}", staplePrice, 90m);

        await repo.WriteAsync(global, null, default);

        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(Slug)), default);

        return new DeckRecommendationService(repo);
    }

    private static RecommendationFilter Filter() =>
        new(ExcludeBasicLands: false, Limit: 300);

    [Fact]
    public async Task Builds_a_complete_99_card_deck()
    {
        var svc = await SeedAsync();
        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), default);

        Assert.Equal(99, deck.CardCount);
        Assert.Equal(99, deck.Cards.Count);
    }

    [Fact]
    public async Task Never_exceeds_the_budget()
    {
        var svc = await SeedAsync();
        foreach (var budget in new[] { 20m, 50m, 100m, 500m })
        {
            var deck = await svc.BuildBudgetDeckAsync(Slug, budget, Filter(), default);
            Assert.True(deck.TotalPriceEur <= budget,
                $"€{deck.TotalPriceEur} exceeded budget €{budget}");
        }
    }

    [Fact]
    public async Task Completes_manabase_with_basics_in_colour_identity()
    {
        var svc = await SeedAsync();
        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), default);

        var basics = deck.Cards.Where(c => BasicNames.Contains(c.Name)).ToList();
        Assert.Equal(37, basics.Count);
        // Temur identity → only Mountain / Forest / Island, never off-colour basics.
        Assert.All(basics, b =>
            Assert.Contains(b.Name, new[] { "Mountain", "Forest", "Island" }));
    }

    [Fact]
    public async Task Higher_budget_buys_more_staples()
    {
        var svc = await SeedAsync();
        var cheap = await svc.BuildBudgetDeckAsync(Slug, 20m, Filter(), default);
        var rich  = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), default);

        int Staples(BudgetDeck d) => d.Cards.Count(c => (c.InclusionPct ?? 0m) >= 50m);

        Assert.True(Staples(rich) > Staples(cheap),
            $"expected the €100 deck to include more staples than the €20 deck " +
            $"(got {Staples(rich)} vs {Staples(cheap)})");
        Assert.True(rich.TotalPriceEur > cheap.TotalPriceEur);
    }

    [Fact]
    public async Task Attaches_a_bracket_estimate()
    {
        var svc = await SeedAsync();
        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), default);

        Assert.NotNull(deck.Bracket);
        Assert.InRange(deck.Bracket!.Level, 1, 5);
        Assert.True(deck.Bracket.IsEstimate);
    }
}
