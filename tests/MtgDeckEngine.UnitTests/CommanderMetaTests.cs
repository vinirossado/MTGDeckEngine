using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class CommanderMetaTests
{
    private const string Slug = "test-commander";

    private static void AddEntry(
        RdfGraph g, string deckId, int wins, int losses, int placement, int topCutSize = 16)
    {
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri("test", deckId)));
        var entry = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri("test", "T1", deckId)));
        var commander = g.CreateUriNode(new Uri(MtgVocab.CommanderUri(Slug)));

        void P(INode s, string prop, INode o)
            => g.Assert(s, g.CreateUriNode(new Uri(MtgVocab.Property(prop))), o);
        ILiteralNode I(int i) => g.CreateLiteralNode(
            i.ToString(), new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger));

        P(deck, "hasCommander", commander);
        P(entry, "hasDeck", deck);
        P(entry, "hasPlacement", I(placement));
        P(entry, "hasWinsSwiss", I(wins));
        P(entry, "hasLossesSwiss", I(losses));

        var tournament = g.CreateUriNode(new Uri(MtgVocab.TournamentUri("test", $"T-{deckId}")));
        P(entry, "inTournament", tournament);
        P(tournament, "hasTopCutSize", I(topCutSize));
    }

    private static void AddAggregates(RdfGraph g, int entries, int topCuts, decimal winRate)
    {
        var commander = g.CreateUriNode(new Uri(MtgVocab.CommanderUri(Slug)));
        void P(string prop, INode o)
            => g.Assert(commander, g.CreateUriNode(new Uri(MtgVocab.Property(prop))), o);

        P("hasTournamentEntryCount", g.CreateLiteralNode(
            entries.ToString(), new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger)));
        P("hasTournamentTopCutCount", g.CreateLiteralNode(
            topCuts.ToString(), new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger)));
        P("hasTournamentWinRate", g.CreateLiteralNode(
            RdfLiterals.Decimal(winRate), new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal)));
    }

    private static async Task<DeckRecommendationService> SeedAsync(Action<RdfGraph> seed)
    {
        var repo = new InMemoryGraphRepository();
        var g = new RdfGraph();
        seed(g);
        await repo.WriteAsync(g, null, default);
        return new DeckRecommendationService(repo);
    }

    [Fact]
    public async Task Derives_metrics_when_no_aggregates_exist()
    {
        // TopDeck-sourced commanders have tournament decks but no EDHTop16
        // aggregate. They used to report a flat zero, which reads as "never
        // played" rather than "no aggregate available".
        var svc = await SeedAsync(g =>
        {
            AddEntry(g, "d0", 7, 3, placement: 2);    // top cut
            AddEntry(g, "d1", 5, 5, placement: 40);
            AddEntry(g, "d2", 6, 4, placement: 9);    // top cut
            AddEntry(g, "d3", 2, 8, placement: 55);
        });

        var meta = await svc.GetCommanderMetaAsync(Slug, default);

        Assert.Equal(CommanderMetaSource.DerivedFromEntries, meta.Source);
        Assert.Equal(4, meta.TournamentEntryCount);
        Assert.Equal(2, meta.TopCutCount);
        Assert.Equal(0.5m, meta.WinRate);            // 20 wins of 40 games
        Assert.Equal(0.5m, meta.ConversionRate);     // 2 of 4 decks
    }

    [Fact]
    public async Task Top_cut_is_measured_against_the_events_own_cut()
    {
        // Placing 8th is a conversion in a top-8 event and is not in a top-4 one.
        // Assuming a flat 16 reported ~92% conversion for commanders that had
        // merely attended small tournaments.
        var svc = await SeedAsync(g =>
        {
            AddEntry(g, "d0", 5, 5, placement: 8, topCutSize: 8);   // made the cut
            AddEntry(g, "d1", 5, 5, placement: 8, topCutSize: 4);   // did not
        });

        var meta = await svc.GetCommanderMetaAsync(Slug, default);

        Assert.Equal(1, meta.TopCutCount);
        Assert.Equal(0.5m, meta.ConversionRate);
    }

    [Fact]
    public async Task Prefers_the_aggregate_over_deriving()
    {
        // The aggregate covers all of EDHTop16; our entries are a subset. When
        // both exist the broader figure wins.
        var svc = await SeedAsync(g =>
        {
            AddEntry(g, "d0", 7, 3, placement: 2);
            AddAggregates(g, entries: 500, topCuts: 90, winRate: 0.31m);
        });

        var meta = await svc.GetCommanderMetaAsync(Slug, default);

        Assert.Equal(CommanderMetaSource.EdhTop16Aggregate, meta.Source);
        Assert.Equal(500, meta.TournamentEntryCount);
        Assert.Equal(90, meta.TopCutCount);
        Assert.Equal(0.31m, meta.WinRate);
    }

    [Fact]
    public async Task Reports_no_source_for_a_commander_we_know_nothing_about()
    {
        var svc = await SeedAsync(_ => { });

        var meta = await svc.GetCommanderMetaAsync("never-heard-of-it", default);

        Assert.Equal(CommanderMetaSource.None, meta.Source);
        Assert.Equal(0, meta.TournamentEntryCount);
        Assert.Null(meta.WinRate);
    }

    [Fact]
    public async Task Meta_share_is_never_derived()
    {
        // Our slice of the format is not the format, so a derived "meta share"
        // would be a made-up number.
        var svc = await SeedAsync(g => AddEntry(g, "d0", 7, 3, placement: 2));

        var meta = await svc.GetCommanderMetaAsync(Slug, default);

        Assert.Equal(CommanderMetaSource.DerivedFromEntries, meta.Source);
        Assert.Null(meta.MetaShare);
    }
}
