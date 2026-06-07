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
}
