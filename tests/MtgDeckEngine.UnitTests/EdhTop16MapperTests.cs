using MtgDeckEngine.Core;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Mappers;
using VDS.RDF;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class EdhTop16MapperTests
{
    [Fact]
    public void Asserts_commander_aggregate_stats()
    {
        var g = new RdfGraph();
        var commander = new EdhTop16Commander
        {
            Name = "Xyris, the Writhing Storm",
            ColorId = "URG",
            Stats = new EdhTop16CommanderStats
            {
                Count = 38, TopCuts = 7,
                MetaShare = 0.000417m, WinRate = 0.192m, ConversionRate = 0.184m
            }
        };
        EdhTop16ToRdfMapper.AssertCommanderStats(g, "xyris-the-writhing-storm", commander);

        var subj = g.CreateUriNode(new Uri(MtgVocab.CommanderUri("xyris-the-writhing-storm")));
        var entryCountProp = g.CreateUriNode(new Uri(MtgVocab.Property("hasTournamentEntryCount")));
        Assert.Single(g.GetTriplesWithSubjectPredicate(subj, entryCountProp));
    }

    [Fact]
    public void Asserts_tournament_entry_with_deck_and_cards()
    {
        var g = new RdfGraph();
        var entry = new EdhTop16Entry
        {
            Id = "abc123", Standing = 2,
            Wins = 4, Losses = 3, Draws = 0,
            WinsSwiss = 3, WinsBracket = 1, LossesSwiss = 2, LossesBracket = 1,
            Decklist = "https://topdeck.gg/deck/xyz",
            PriceUsd = 11983.39m,
            Tournament = new EdhTop16Tournament
            {
                TID = "space-coast-treasure-cedh-tournament",
                Name = "Space Coast Treasures cEDH",
                TournamentDate = "2026-03-28T15:00:00.000Z",
                Size = 58, TopCut = 16, SwissRounds = 5
            },
            Maindeck = new()
            {
                new EdhTop16Card { Name = "Sol Ring",   OracleId = "6ad8011d-3471-4369-9d68-b264cc027487" },
                new EdhTop16Card { Name = "Cultivate",  OracleId = "8b755881-a72d-4e21-a369-d2924eb4585a" },
                new EdhTop16Card { Name = "Tokenless",  OracleId = null }, // skip
            },
            Player = new EdhTop16Player { Name = "Alice" }
        };
        EdhTop16ToRdfMapper.AssertEntry(g, "xyris-the-writhing-storm", entry);

        // Tournament asserted as a Tournament with hasName / hasDate / hasTopCutSize.
        var t = g.CreateUriNode(new Uri(MtgVocab.TournamentUri("edhtop16", entry.Tournament.TID)));
        Assert.NotEmpty(g.GetTriplesWithSubject(t));

        // Deck contains exactly the 2 cards with oracle ids.
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri("edhtop16", $"{entry.Tournament.TID}/abc123")));
        var contains = g.CreateUriNode(new Uri(MtgVocab.Property("containsCard")));
        var cardTriples = g.GetTriplesWithSubjectPredicate(deck, contains).ToList();
        Assert.Equal(2, cardTriples.Count);

        // TournamentEntry references the deck + has placement.
        var entryUri = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri("edhtop16",
            entry.Tournament.TID, "abc123")));
        Assert.NotEmpty(g.GetTriplesWithSubject(entryUri));
    }

    [Fact]
    public void Skips_entries_without_tournament_metadata()
    {
        var g = new RdfGraph();
        var entry = new EdhTop16Entry { Standing = 1, Tournament = null };
        EdhTop16ToRdfMapper.AssertEntry(g, "x", entry);
        Assert.Empty(g.Triples);
    }
}
