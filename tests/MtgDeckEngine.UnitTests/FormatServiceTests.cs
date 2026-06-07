using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Mappers;
using VDS.RDF;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class FormatServiceTests
{
    [Fact]
    public async Task Lists_distinct_formats_and_counts()
    {
        var repo = new InMemoryGraphRepository();
        // Two EDH tournaments, one Modern tournament.
        var g = new RdfGraph();
        AssertTournament(g, "edhtop16", "t1", "EDH");
        AssertTournament(g, "topdeck", "t2", "EDH");
        AssertTournament(g, "topdeck", "t3", "MODERN");
        await repo.WriteAsync(g, null, default);

        var svc = new FormatService(repo);
        var formats = await svc.ListFormatsAsync(default);
        var byKey = formats.ToDictionary(f => f.Format);
        Assert.Equal(2, byKey["EDH"].TournamentCount);
        Assert.Equal(1, byKey["MODERN"].TournamentCount);
    }

    [Fact]
    public async Task Format_meta_aggregates_top_cut_counts()
    {
        var repo = new InMemoryGraphRepository();
        var g = new RdfGraph();
        AssertTournament(g, "topdeck", "t-modern", "MODERN");
        AssertEntry(g, "topdeck", "t-modern", "p1", placement: 2, deckCard: ("ring-oid", "Sol Ring"));
        AssertEntry(g, "topdeck", "t-modern", "p2", placement: 5, deckCard: ("ring-oid", "Sol Ring"));
        await repo.WriteAsync(g, null, default);

        var svc = new FormatService(repo);
        var meta = await svc.GetFormatMetaAsync("MODERN", default);
        Assert.Equal(1, meta.TournamentCount);
        Assert.Equal(2, meta.DeckCount);
        Assert.Equal(1, meta.Top4DeckCount); // only placement 2
        Assert.Equal(2, meta.Top16DeckCount);
    }

    private static void AssertTournament(IGraph g, string source, string tid, string format)
    {
        var t = g.CreateUriNode(new Uri(MtgVocab.TournamentUri(source, tid)));
        g.Assert(t, g.CreateUriNode(new Uri(VDS.RDF.Parsing.RdfSpecsHelper.RdfType)),
            g.CreateUriNode(new Uri(MtgVocab.Class("Tournament"))));
        g.Assert(t, g.CreateUriNode(new Uri(MtgVocab.Property("hasFormat"))),
            g.CreateLiteralNode(format));
        g.Assert(t, g.CreateUriNode(new Uri(MtgVocab.Property("hasName"))),
            g.CreateLiteralNode($"Test {tid}"));
    }

    private static void AssertEntry(IGraph g, string source, string tid, string entryKey,
        int placement, (string OracleId, string Name) deckCard)
    {
        var t = g.CreateUriNode(new Uri(MtgVocab.TournamentUri(source, tid)));
        var entry = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri(source, tid, entryKey)));
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri(source, $"{tid}/{entryKey}")));
        var card = g.CreateUriNode(new Uri(MtgVocab.CardUri(deckCard.OracleId)));

        g.Assert(entry, g.CreateUriNode(new Uri(MtgVocab.Property("inTournament"))), t);
        g.Assert(entry, g.CreateUriNode(new Uri(MtgVocab.Property("hasDeck"))), deck);
        g.Assert(entry, g.CreateUriNode(new Uri(MtgVocab.Property("hasPlacement"))),
            g.CreateLiteralNode(placement.ToString(),
                new Uri(VDS.RDF.Parsing.XmlSpecsHelper.XmlSchemaDataTypeInteger)));
        g.Assert(deck, g.CreateUriNode(new Uri(MtgVocab.Property("containsCard"))), card);
        g.Assert(card, g.CreateUriNode(new Uri(MtgVocab.Property("hasOracleId"))),
            g.CreateLiteralNode(deckCard.OracleId));
        g.Assert(card, g.CreateUriNode(new Uri(MtgVocab.Property("hasName"))),
            g.CreateLiteralNode(deckCard.Name));
    }
}
