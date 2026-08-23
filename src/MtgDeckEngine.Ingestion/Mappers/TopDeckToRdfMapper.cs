using System.Globalization;
using MtgDeckEngine.Core;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Http;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace MtgDeckEngine.Ingestion.Mappers;

public static class TopDeckToRdfMapper
{
    private const string Source = "topdeck";

    /// <summary>
    /// Assert one tournament with all standings, decks, and per-entry results.
    /// Card name → oracleId resolution uses the in-memory Scryfall bulk cache;
    /// unresolved cards are skipped (logged by the worker via miss counts).
    /// </summary>
    public static int AssertTournament(
        IGraph g,
        TopDeckTournament t,
        ScryfallBulkCache scryfall,
        out int unresolvedCards)
    {
        unresolvedCards = 0;
        if (string.IsNullOrWhiteSpace(t.TID)) return 0;

        var tournament = g.CreateUriNode(new Uri(MtgVocab.TournamentUri(Source, t.TID)));
        AssertType(g, tournament, "Tournament");
        Assert(g, tournament, "hasName", g.CreateLiteralNode(t.Name));
        if (t.StartDate is long ts)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
            Assert(g, tournament, "hasDate", Date(g, DateOnly.FromDateTime(date)));
        }
        if (t.Standings is { Count: > 0 })
            Assert(g, tournament, "hasPlayerCount", Int(g, t.Standings.Count));
        if (t.TopCut is int tc) Assert(g, tournament, "hasTopCutSize", Int(g, tc));
        if (t.SwissRounds is int sr) Assert(g, tournament, "hasSwissRounds", Int(g, sr));
        var fmt = NormalizeFormat(t.Format);
        Assert(g, tournament, "hasFormat", g.CreateLiteralNode(fmt));

        var entriesAdded = 0;
        if (t.Standings is null) return 0;
        // TopDeck's search endpoint doesn't return per-standing `standing`
        // (that field is only on the /standings sub-resource). The array
        // itself is ordered by rank, so use the index as the placement.
        for (var i = 0; i < t.Standings.Count; i++)
        {
            var s = t.Standings[i];
            var placement = s.Standing ?? (i + 1);
            var (entriesDelta, missesDelta) = AssertStanding(g, tournament, t.TID, fmt, s, placement, scryfall);
            entriesAdded += entriesDelta;
            unresolvedCards += missesDelta;
        }
        return entriesAdded;
    }

    private static (int Entries, int Misses) AssertStanding(
        IGraph g,
        IUriNode tournament,
        string tournamentId,
        string format,
        TopDeckStanding s,
        int placement,
        ScryfallBulkCache scryfall)
    {
        var entryKey = !string.IsNullOrWhiteSpace(s.Id)
            ? Slug(s.Id!)
            : $"p{placement}_{Slug(s.Name)}";
        var entryUri = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri(Source, tournamentId, entryKey)));
        AssertType(g, entryUri, "TournamentEntry");
        Assert(g, entryUri, "inTournament", tournament);
        Assert(g, entryUri, "hasPlacement", Int(g, placement));
        if (s.Wins is int w) Assert(g, entryUri, "hasWinsSwiss", Int(g, w));
        if (s.WinsSwiss is int ws) Assert(g, entryUri, "hasWinsSwiss", Int(g, ws));
        if (s.LossesSwiss is int ls) Assert(g, entryUri, "hasLossesSwiss", Int(g, ls));
        if (s.WinsBracket is int wb) Assert(g, entryUri, "hasWinsBracket", Int(g, wb));
        if (s.LossesBracket is int lb) Assert(g, entryUri, "hasLossesBracket", Int(g, lb));
        if (s.Draws is int d) Assert(g, entryUri, "hasDraws", Int(g, d));
        if (!string.IsNullOrWhiteSpace(s.Name))
            Assert(g, entryUri, "hasPilotName", g.CreateLiteralNode(s.Name));

        // Deck
        var deck = g.CreateUriNode(new Uri(MtgVocab.DeckUri(Source, $"{tournamentId}/{entryKey}")));
        AssertType(g, deck, "Deck");
        Assert(g, deck, "hasSource", g.CreateLiteralNode(Source));
        Assert(g, deck, "hasFormat", g.CreateLiteralNode(format));
        Assert(g, entryUri, "hasDeck", deck);

        var misses = 0;
        if (!string.IsNullOrWhiteSpace(s.Decklist))
        {
            // Decklist is a string. Common forms:
            //   "https://moxfield.com/decks/abc"  → URL only, no card list
            //   "1 Sol Ring\n1 Cultivate\n..."    → text list we can parse
            if (Uri.TryCreate(s.Decklist, UriKind.Absolute, out var url))
            {
                Assert(g, entryUri, "hasDecklistUrl",
                    g.CreateLiteralNode(s.Decklist!, new Uri(XmlSpecsHelper.XmlSchemaDataTypeAnyUri)));
            }
            else
            {
                // Deck-level rollups are accumulated here and asserted once
                // below. Deriving them at query time instead would mean summing
                // ~100 card prices per deck across tens of thousands of decks on
                // every request — the discovery query would be unusable.
                var totalEur = 0m;
                var pricedCards = 0;
                var gameChangers = 0;

                foreach (var (name, qty, isCommander) in ParseTextDecklist(s.Decklist!))
                {
                    if (scryfall.TryGetByName(name, out var card) && card.OracleId is { Length: > 0 })
                    {
                        if (card.BestPrice is decimal unit) { totalEur += unit * qty; pricedCards++; }
                        if (card.GameChanger) gameChangers += qty;

                        var cardNode = g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId)));
                        g.Assert(deck,
                            g.CreateUriNode(new Uri(MtgVocab.Property("containsCard"))),
                            cardNode);

                        // Link the deck to its commander. Partner/background
                        // pairs put two cards in the command zone, so this is
                        // asserted per card rather than assuming one.
                        if (isCommander)
                        {
                            Assert(g, deck, "hasCommander",
                                g.CreateUriNode(new Uri(MtgVocab.CommanderUri(MtgVocab.Slugify(card.Name)))));
                            // And make that commander URI resolvable: without a
                            // name and type on it, every commander-facing query
                            // sees a bare URI it cannot label.
                            AssertCommanderIdentity(g, card);
                        }

                        // Also write the global card facts (oracleId / name / type /
                        // price / image URL). INSERT DATA is idempotent for identical
                        // triples so duplicating across decks is a no-op. Without
                        // this, Modern/Legacy cards we don't otherwise touch via
                        // EDHREC stay nameless in the graph and don't show up in
                        // format-staples queries.
                        ScryfallToRdfMapper.AssertCard(g, ScryfallToRdfMapper.ToDto(card));
                    }
                    else
                    {
                        misses++;
                    }
                }

                if (pricedCards > 0)
                    Assert(g, deck, "hasTotalPriceEur", DecimalNode(g, totalEur));
                // Written even when zero: "no Game Changers" is a fact the
                // bracket filter needs, and an absent triple is indistinguishable
                // from a deck we simply could not price.
                Assert(g, deck, "hasGameChangerCount", Int(g, gameChangers));
                Assert(g, deck, "hasPricedCardCount", Int(g, pricedCards));
            }
        }
        return (1, misses);
    }

    /// <summary>
    /// Parse common text-decklist forms:
    ///   "1 Sol Ring"
    ///   "1x Sol Ring"
    ///   "Sol Ring"
    ///
    /// TopDeck.gg wraps EDH lists in markdown section headers, and the command
    /// zone is the only place the commander is identified:
    ///
    ///   ~~Commanders~~
    ///   1 Kinnan, Bonder Prodigy
    ///
    ///   ~~Mainboard~~
    ///   1 Arcane Signet
    ///
    /// Cards in that section come back with <c>IsCommander</c> set. Skipping
    /// the header and treating the commander as an ordinary card — the previous
    /// behaviour — threw away the single most useful fact about an EDH deck.
    ///
    /// Empty lines, comments and sideboard headers are skipped. Also handles
    /// TopDeck.gg's literal "\n" backslash-n escape: their payload arrives as
    /// one string with embedded escapes rather than real newlines.
    /// </summary>
    public static IEnumerable<(string Name, int Qty, bool IsCommander)> ParseTextDecklist(string text)
    {
        // Real newlines OR literal "\n" (TopDeck.gg payload form).
        var normalised = text
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\'", "'", StringComparison.Ordinal);

        var inCommandZone = false;

        foreach (var raw in normalised.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("//") || line.StartsWith('#')) continue;
            if (line.StartsWith("Sideboard", StringComparison.OrdinalIgnoreCase)) yield break;

            // Section headers, with or without the markdown "~~" fence. Matched
            // on the bare word so a card line ("1 Commander's Sphere") is never
            // mistaken for a header — those start with a quantity.
            if (IsHeader(line, "Commander", "Commanders")) { inCommandZone = true;  continue; }
            if (IsHeader(line, "Mainboard", "Deck", "Maindeck")) { inCommandZone = false; continue; }
            if (line.StartsWith("~~")) continue;   // any other markdown section

            // Strip optional set/collector suffix: "Sol Ring (CMR) 472"
            var parenIdx = line.IndexOf('(');
            if (parenIdx > 0) line = line[..parenIdx].TrimEnd();

            // "1 ", "1x ", "12 ", etc.
            var firstSpace = line.IndexOf(' ');
            if (firstSpace < 1)
            {
                yield return (line, 1, inCommandZone);
                continue;
            }
            var qtyPart = line[..firstSpace].TrimEnd('x', 'X');
            var namePart = line[(firstSpace + 1)..].Trim();
            if (int.TryParse(qtyPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
                yield return (namePart, qty, inCommandZone);
            else
                yield return (line, 1, inCommandZone);
        }
    }

    /// <summary>
    /// Assert the commander URI as a first-class node: typed, named, and tied
    /// back to the card it came from. EDHREC-ingested commanders get this via
    /// the commander context; TopDeck-only ones would otherwise be nameless.
    /// </summary>
    private static void AssertCommanderIdentity(IGraph g, Dto.ScryfallCard card)
    {
        var commander = g.CreateUriNode(new Uri(MtgVocab.CommanderUri(MtgVocab.Slugify(card.Name))));
        AssertType(g, commander, "Commander");
        Assert(g, commander, "hasName", g.CreateLiteralNode(card.Name));
        if (card.OracleId is { Length: > 0 })
        {
            Assert(g, commander, "hasOracleId", g.CreateLiteralNode(card.OracleId));
            Assert(g, commander, "isCardOf",
                g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId))));
        }
    }

    /// <summary>
    /// True when the line is a bare section header, optionally fenced in "~~".
    /// Card lines carry a quantity prefix, so they never match.
    /// </summary>
    private static bool IsHeader(string line, params string[] names)
    {
        var bare = line.Trim('~', ' ', ':');
        foreach (var n in names)
            if (bare.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string NormalizeFormat(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? "UNKNOWN"
            : raw.ToUpperInvariant().Replace(' ', '_');

    private static void AssertType(IGraph g, INode subj, string localClass)
        => g.Assert(subj,
            g.CreateUriNode(new Uri(RdfSpecsHelper.RdfType)),
            g.CreateUriNode(new Uri(MtgVocab.Class(localClass))));

    private static void Assert(IGraph g, INode subj, string localProp, INode obj)
        => g.Assert(subj,
            g.CreateUriNode(new Uri(MtgVocab.Property(localProp))),
            obj);

    private static ILiteralNode DecimalNode(IGraph g, decimal d) =>
        g.CreateLiteralNode(MtgDeckEngine.Core.RdfLiterals.Decimal(d),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode Int(IGraph g, int i) =>
        g.CreateLiteralNode(i.ToString(CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger));

    private static ILiteralNode Date(IGraph g, DateOnly d) =>
        g.CreateLiteralNode(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDate));

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
