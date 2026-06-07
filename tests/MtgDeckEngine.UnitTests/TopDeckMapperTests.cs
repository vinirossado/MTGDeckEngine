using System.Text.Json;
using MtgDeckEngine.Core;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Http;
using MtgDeckEngine.Ingestion.Mappers;
using VDS.RDF;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class TopDeckMapperTests
{
    [Fact]
    public void Parses_quantity_prefix_decklist_lines()
    {
        var text = """
            1 Sol Ring
            1x Cultivate
            4 Lightning Bolt
            // comment
            Sideboard
            2 Mountain
            """;
        var parsed = TopDeckToRdfMapper.ParseTextDecklist(text).ToList();
        Assert.Contains(parsed, p => p.Name == "Sol Ring" && p.Qty == 1);
        Assert.Contains(parsed, p => p.Name == "Cultivate" && p.Qty == 1);
        Assert.Contains(parsed, p => p.Name == "Lightning Bolt" && p.Qty == 4);
        // Should stop at Sideboard.
        Assert.DoesNotContain(parsed, p => p.Name == "Mountain");
    }

    [Fact]
    public void Strips_set_collector_suffix()
    {
        var line = "1 Sol Ring (CMR) 472";
        var parsed = TopDeckToRdfMapper.ParseTextDecklist(line).ToList();
        Assert.Single(parsed);
        Assert.Equal("Sol Ring", parsed[0].Name);
    }

    [Fact]
    public void Format_normalisation_collapses_spaces_to_underscores()
    {
        Assert.Equal("EDH", TopDeckToRdfMapper.NormalizeFormat("EDH"));
        Assert.Equal("DUEL_COMMANDER", TopDeckToRdfMapper.NormalizeFormat("Duel Commander"));
        Assert.Equal("UNKNOWN", TopDeckToRdfMapper.NormalizeFormat(null));
    }

    [Fact]
    public void Asserts_tournament_with_standing_and_decklist_url()
    {
        var g = new RdfGraph();
        var t = new TopDeckTournament
        {
            TID = "tt-1", Name = "Test EDH",
            StartDate = 1733616000, // 2024-12-08 (matches Unix seconds)
            Format = "EDH", TopCut = 8, SwissRounds = 4,
            Standings = new()
            {
                new TopDeckStanding
                {
                    Name = "Alice", Id = "alice-123", Standing = 1,
                    Wins = 4, Losses = 0, Draws = 0,
                    Decklist = "https://moxfield.com/decks/abc"
                }
            }
        };
        var added = TopDeckToRdfMapper.AssertTournament(g, t, new ScryfallBulkCache(
            new HttpClient(), new TestLogger<ScryfallBulkCache>()), out var misses);
        Assert.Equal(1, added);
        Assert.Equal(0, misses); // no text decklist to resolve

        var tournament = g.CreateUriNode(new Uri(MtgVocab.TournamentUri("topdeck", "tt-1")));
        Assert.NotEmpty(g.GetTriplesWithSubject(tournament));

        // Deck carries the format.
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri("topdeck", "tt-1/alice-123")));
        var hasFormat = g.CreateUriNode(new Uri(MtgVocab.Property("hasFormat")));
        var fmt = g.GetTriplesWithSubjectPredicate(deck, hasFormat).Single();
        Assert.Equal("EDH", ((ILiteralNode)fmt.Object).Value);
    }
}

// Minimal stub so we can newup ScryfallBulkCache without DI for parsing-only tests.
internal sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => false;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
        TState state, Exception? ex, Func<TState, Exception?, string> formatter) { }
}
