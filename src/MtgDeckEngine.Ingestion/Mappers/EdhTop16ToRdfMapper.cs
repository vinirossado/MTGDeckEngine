using System.Globalization;
using MtgDeckEngine.Core;
using MtgDeckEngine.Ingestion.Dto;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace MtgDeckEngine.Ingestion.Mappers;

public static class EdhTop16ToRdfMapper
{
    private const string Source = "edhtop16";

    /// <summary>
    /// Assert commander-level aggregates onto the commander URI. Lives in the
    /// default graph because tournament performance is a fact about the
    /// commander, not commander-context opinion data.
    /// </summary>
    public static void AssertCommanderStats(
        IGraph g,
        string commanderSlug,
        EdhTop16Commander src)
    {
        var commander = g.CreateUriNode(new Uri(MtgVocab.CommanderUri(commanderSlug)));
        var s = src.Stats;
        if (s is null) return;
        Assert(g, commander, "hasTournamentEntryCount", Int(g, s.Count));
        Assert(g, commander, "hasTournamentTopCutCount", Int(g, s.TopCuts));
        Assert(g, commander, "hasTournamentWinRate", Decimal(g, s.WinRate));
        Assert(g, commander, "hasTournamentConversionRate", Decimal(g, s.ConversionRate));
        Assert(g, commander, "hasMetaShare", Decimal(g, s.MetaShare));
    }

    /// <summary>
    /// Assert one tournament + its single entry + its deck (containsCard for each
    /// resolved oracleId). Entries with missing tournament metadata are skipped.
    /// </summary>
    public static void AssertEntry(IGraph g, string commanderSlug, EdhTop16Entry e)
    {
        if (e.Tournament is null || string.IsNullOrWhiteSpace(e.Tournament.TID)) return;

        var t = e.Tournament;
        var tournament = g.CreateUriNode(new Uri(MtgVocab.TournamentUri(Source, t.TID)));
        AssertType(g, tournament, "Tournament");
        Assert(g, tournament, "hasName", g.CreateLiteralNode(t.Name));
        if (DateOnly.TryParse(t.TournamentDate ?? "", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            Assert(g, tournament, "hasDate", Date(g, date));
        Assert(g, tournament, "hasPlayerCount", Int(g, t.Size));
        Assert(g, tournament, "hasTopCutSize", Int(g, t.TopCut));
        Assert(g, tournament, "hasSwissRounds", Int(g, t.SwissRounds));
        Assert(g, tournament, "hasFormat", g.CreateLiteralNode("COMMANDER"));

        // Deck — keyed by tournament + entry id (or placement+player fallback).
        var entryKey = !string.IsNullOrWhiteSpace(e.Id) ? Slug(e.Id) : $"p{e.Standing}";
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri(Source, $"{t.TID}/{entryKey}")));
        AssertType(g, deck, "Deck");
        Assert(g, deck, "hasSource", g.CreateLiteralNode(Source));
        Assert(g, deck, "hasCommander",
            g.CreateUriNode(new Uri(MtgVocab.CommanderUri(commanderSlug))));
        if (e.PriceUsd is decimal usd)
            Assert(g, deck, "hasTotalPriceUsd", Decimal(g, usd));
        var cardsAdded = 0;
        if (e.Maindeck is not null)
        {
            foreach (var card in e.Maindeck)
            {
                if (string.IsNullOrWhiteSpace(card.OracleId)) continue;
                var cardNode = g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId)));
                g.Assert(deck,
                    g.CreateUriNode(new Uri(MtgVocab.Property("containsCard"))),
                    cardNode);

                // Minimal global card facts so the card has a name in queries
                // even if EDHREC didn't surface it. Image URL would need the
                // Scryfall bulk cache to be passed in — left for now; cards
                // also appearing in EDHREC will have images via that path.
                Assert(g, cardNode, "hasOracleId", g.CreateLiteralNode(card.OracleId!));
                if (!string.IsNullOrWhiteSpace(card.Name))
                    Assert(g, cardNode, "hasName", g.CreateLiteralNode(card.Name));
                AssertType(g, cardNode, "Card");
                cardsAdded++;
            }
        }

        // TournamentEntry — joins the player's deck to the tournament + records placement.
        var entryUri = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri(Source, t.TID, entryKey)));
        AssertType(g, entryUri, "TournamentEntry");
        Assert(g, entryUri, "inTournament", tournament);
        Assert(g, entryUri, "hasDeck", deck);
        Assert(g, entryUri, "hasPlacement", Int(g, e.Standing));
        Assert(g, entryUri, "hasWinsSwiss", Int(g, e.WinsSwiss));
        Assert(g, entryUri, "hasLossesSwiss", Int(g, e.LossesSwiss));
        Assert(g, entryUri, "hasWinsBracket", Int(g, e.WinsBracket));
        Assert(g, entryUri, "hasLossesBracket", Int(g, e.LossesBracket));
        Assert(g, entryUri, "hasDraws", Int(g, e.Draws));
        if (!string.IsNullOrWhiteSpace(e.Player?.Name))
            Assert(g, entryUri, "hasPilotName", g.CreateLiteralNode(e.Player.Name!));
        if (!string.IsNullOrWhiteSpace(e.Decklist))
            Assert(g, entryUri, "hasDecklistUrl",
                g.CreateLiteralNode(e.Decklist!, new Uri(XmlSpecsHelper.XmlSchemaDataTypeAnyUri)));
        _ = cardsAdded;
    }

    private static void AssertType(IGraph g, INode subj, string localClass)
        => g.Assert(subj,
            g.CreateUriNode(new Uri(RdfSpecsHelper.RdfType)),
            g.CreateUriNode(new Uri(MtgVocab.Class(localClass))));

    private static void Assert(IGraph g, INode subj, string localProp, INode obj)
        => g.Assert(subj,
            g.CreateUriNode(new Uri(MtgVocab.Property(localProp))),
            obj);

    private static ILiteralNode Int(IGraph g, int i) =>
        g.CreateLiteralNode(i.ToString(CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger));

    private static ILiteralNode Decimal(IGraph g, decimal d) =>
        g.CreateLiteralNode(MtgDeckEngine.Core.RdfLiterals.Decimal(d),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode Date(IGraph g, DateOnly d) =>
        g.CreateLiteralNode(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDate));

    // Safe URI-fragment-friendly slug for free-form ids.
    private static string Slug(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            buf[i] = (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
                ? c
                : '_';
        }
        return new string(buf);
    }
}
