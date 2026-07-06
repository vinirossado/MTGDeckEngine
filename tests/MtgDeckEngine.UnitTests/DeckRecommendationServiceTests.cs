using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Mappers;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class DeckRecommendationServiceTests
{
    [Fact]
    public async Task Returns_results_ordered_by_inclusion_and_respecting_budget()
    {
        var repo = new InMemoryGraphRepository();
        var slug = "xyris-the-writhing-storm";

        // Global card graph: two cards, one cheap, one expensive.
        var globalGraph = new RdfGraph();
        ScryfallToRdfMapper.AssertCard(globalGraph, new CardDto(
            "cheap-oid", "Windfall", new[] { "U" }, new[] { "U" }, "Sorcery",
            null, 3.20m, null, true));
        ScryfallToRdfMapper.AssertCard(globalGraph, new CardDto(
            "pricey-oid", "Wheel of Fortune", new[] { "R" }, new[] { "R" }, "Sorcery",
            null, 40.00m, null, true));
        await repo.WriteAsync(globalGraph, null, default);

        // Commander-scoped EDHREC signal graph.
        var entries = new[]
        {
            new EdhrecCardEntry("Windfall",         slug, "wheel", 78m, 2.1m, 1000, 1200),
            new EdhrecCardEntry("Wheel of Fortune", slug, "wheel", 92m, 2.9m, 1100, 1200),
        };
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Windfall"] = "cheap-oid",
            ["Wheel of Fortune"] = "pricey-oid",
        };
        var contextGraph = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(contextGraph, entries, nameToId);
        await repo.WriteAsync(contextGraph, new Uri(MtgVocab.CommanderContextUri(slug)), default);

        var svc = new DeckRecommendationService(repo);
        var results = await svc.GetRecommendationsAsync(
            slug,
            new RecommendationFilter(MaxPriceEur: 5m, ExcludeBasicLands: false),
            ct: default);

        Assert.Single(results);
        Assert.Equal("Windfall", results[0].Name);
        Assert.Equal(3.20m, results[0].PriceEur);
        Assert.Equal(78m, results[0].InclusionPct);
    }

    // Regression: a card ingested repeatedly (several prices/images) and sitting
    // in multiple EDHREC categories used to multiply the result rows, so LIMIT
    // kept only a few distinct cards and the deck builder starved. The query now
    // folds these to one row per card, taking the cheapest (MIN) price.
    [Fact]
    public async Task Deduplicates_cards_with_multiple_prices_and_categories()
    {
        var repo = new InMemoryGraphRepository();
        var slug = "xyris-the-writhing-storm";

        var global = new RdfGraph();
        // Same oracle id ingested twice → two prices + two images on one node.
        ScryfallToRdfMapper.AssertCard(global, new CardDto(
            "oid-1", "Windfall", new[] { "U" }, new[] { "U" }, "Sorcery",
            null, 5.50m, 6.00m, true, "http://img/a"));
        ScryfallToRdfMapper.AssertCard(global, new CardDto(
            "oid-1", "Windfall", new[] { "U" }, new[] { "U" }, "Sorcery",
            null, 3.20m, 4.00m, true, "http://img/b"));
        await repo.WriteAsync(global, null, default);

        // Same card in two EDHREC categories.
        var entries = new[]
        {
            new EdhrecCardEntry("Windfall", slug, "wheel", 78m, 2.1m, 1000, 1200) { CategoryLabel = "Wheels" },
            new EdhrecCardEntry("Windfall", slug, "draw",  78m, 2.1m, 1000, 1200) { CategoryLabel = "Card Draw" },
        };
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Windfall"] = "oid-1",
        };
        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(slug)), default);

        var svc = new DeckRecommendationService(repo);
        var results = await svc.GetRecommendationsAsync(
            slug, new RecommendationFilter(ExcludeBasicLands: false, Limit: 50), default);

        Assert.Single(results);                    // one row, not 2×2 = four
        Assert.Equal(3.20m, results[0].PriceEur);  // cheapest printing
    }
}
