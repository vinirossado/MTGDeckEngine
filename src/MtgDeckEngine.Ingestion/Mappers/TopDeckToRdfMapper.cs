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
        foreach (var s in t.Standings)
        {
            var (entriesDelta, missesDelta) = AssertStanding(g, tournament, t.TID, fmt, s, scryfall);
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
        ScryfallBulkCache scryfall)
    {
        var entryKey = !string.IsNullOrWhiteSpace(s.Id)
            ? Slug(s.Id!)
            : $"p{s.Standing ?? 0}_{Slug(s.Name)}";
        var entryUri = g.CreateUriNode(new Uri(MtgVocab.TournamentEntryUri(Source, tournamentId, entryKey)));
        AssertType(g, entryUri, "TournamentEntry");
        Assert(g, entryUri, "inTournament", tournament);
        if (s.Standing is int placement) Assert(g, entryUri, "hasPlacement", Int(g, placement));
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
                foreach (var (name, _qty) in ParseTextDecklist(s.Decklist!))
                {
                    if (scryfall.TryGetByName(name, out var card) && card.OracleId is { Length: > 0 })
                    {
                        var cardNode = g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId)));
                        g.Assert(deck,
                            g.CreateUriNode(new Uri(MtgVocab.Property("containsCard"))),
                            cardNode);
                    }
                    else
                    {
                        misses++;
                    }
                }
            }
        }
        return (1, misses);
    }

    /// <summary>
    /// Parse common text-decklist forms:
    ///   "1 Sol Ring"
    ///   "1x Sol Ring"
    ///   "Sol Ring"
    /// Lines that are empty / comments / sideboard headers are skipped.
    /// </summary>
    public static IEnumerable<(string Name, int Qty)> ParseTextDecklist(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("//") || line.StartsWith('#')) continue;
            if (line.StartsWith("Sideboard", StringComparison.OrdinalIgnoreCase)) yield break;
            if (line.StartsWith("Commander", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Deck", StringComparison.OrdinalIgnoreCase)) continue;

            // Strip optional set/collector suffix: "Sol Ring (CMR) 472"
            var parenIdx = line.IndexOf('(');
            if (parenIdx > 0) line = line[..parenIdx].TrimEnd();

            // "1 ", "1x ", "12 ", etc.
            var firstSpace = line.IndexOf(' ');
            if (firstSpace < 1)
            {
                yield return (line, 1);
                continue;
            }
            var qtyPart = line[..firstSpace].TrimEnd('x', 'X');
            var namePart = line[(firstSpace + 1)..].Trim();
            if (int.TryParse(qtyPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
                yield return (namePart, qty);
            else
                yield return (line, 1);
        }
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
