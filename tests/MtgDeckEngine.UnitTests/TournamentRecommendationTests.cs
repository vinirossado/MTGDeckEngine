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

public class TournamentRecommendationTests
{
    [Fact]
    public async Task Tournament_source_returns_cards_from_top_cut_decks()
    {
        var repo = new InMemoryGraphRepository();
        var slug = "xyris-the-writhing-storm";

        // Global card facts.
        var cards = new RdfGraph();
        ScryfallToRdfMapper.AssertCard(cards, new CardDto(
            "ring-oid", "Sol Ring", new[] { "C" }, new[] { "C" }, "Artifact",
            null, 2.31m, null, true));
        ScryfallToRdfMapper.AssertCard(cards, new CardDto(
            "draw-oid", "Windfall", new[] { "U" }, new[] { "U" }, "Sorcery",
            null, 4.88m, null, true));
        await repo.WriteAsync(cards, null, default);

        // Tournament entries: one top-4 deck with both cards.
        var tournaments = new RdfGraph();
        var entry = new EdhTop16Entry
        {
            Id = "e1", Standing = 2,
            Tournament = new EdhTop16Tournament
            {
                TID = "t1", Name = "Test cEDH",
                TournamentDate = "2026-03-01T00:00:00.000Z",
                Size = 50, TopCut = 8, SwissRounds = 4
            },
            Maindeck = new()
            {
                new EdhTop16Card { Name = "Sol Ring", OracleId = "ring-oid" },
                new EdhTop16Card { Name = "Windfall", OracleId = "draw-oid" },
            },
            Player = new EdhTop16Player { Name = "P" }
        };
        EdhTop16ToRdfMapper.AssertEntry(tournaments, slug, entry);
        await repo.WriteAsync(tournaments, null, default);

        var svc = new DeckRecommendationService(repo);

        // Tournament source with Top-4 cap.
        var top4 = await svc.GetRecommendationsAsync(slug,
            new RecommendationFilter(
                Source: RecommendationSource.Tournament,
                MaxPlacement: 4,
                ExcludeBasicLands: false,
                MaxPriceEur: 5m,
                Limit: 25),
            default);
        Assert.Equal(2, top4.Count);
        Assert.All(top4, r => Assert.True(r.TopCutAppearances >= 1));
    }

    [Fact]
    public async Task Commander_meta_aggregates_stats_and_top_counts()
    {
        var repo = new InMemoryGraphRepository();
        var slug = "xyris-the-writhing-storm";

        var stats = new RdfGraph();
        var commander = new EdhTop16Commander
        {
            Name = "Xyris, the Writhing Storm",
            Stats = new EdhTop16CommanderStats
            {
                Count = 38, TopCuts = 7,
                MetaShare = 0.0004m, WinRate = 0.19m, ConversionRate = 0.18m,
            }
        };
        EdhTop16ToRdfMapper.AssertCommanderStats(stats, slug, commander);
        await repo.WriteAsync(stats, null, default);

        var svc = new DeckRecommendationService(repo);
        var meta = await svc.GetCommanderMetaAsync(slug, default);
        Assert.Equal(38, meta.TournamentEntryCount);
        Assert.Equal(7, meta.TopCutCount);
        Assert.Equal(0.19m, meta.WinRate);
    }
}
