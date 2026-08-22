using System.Globalization;
using System.Text;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace MtgDeckEngine.Graph;

/// <summary>
/// Persists user-built decks as RDF in a dedicated named graph
/// (<see cref="MtgVocab.SavedDecksGraphUri"/>), so saved decks live in the same
/// triplestore as everything else and need no extra infrastructure.
///
/// They are deliberately kept out of the default graph: the win-rate and
/// top-cut queries match any <c>mtg:Deck</c> reachable from a
/// <c>mtg:TournamentEntry</c>, and a saved deck has no entry — but keeping it
/// isolated means a future query that walks decks directly still cannot mistake
/// a user's brew for a tournament result.
/// </summary>
public sealed class SavedDeckService(IGraphRepository repo) : ISavedDeckService
{
    private static readonly Uri GraphUri = new(MtgVocab.SavedDecksGraphUri());

    public async Task<SavedDeck> SaveAsync(
        string name,
        string commanderSlug,
        IReadOnlyList<CardRecommendation> cards,
        DeckBracket? bracket,
        string? notes,
        decimal? budgetEur,
        string? commanderName = null,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var savedAt = DateTimeOffset.UtcNow;
        var total = cards.Sum(c => c.PriceEur ?? 0m);
        var deck = new SavedDeck(
            Id:             id,
            Name:           string.IsNullOrWhiteSpace(name) ? DefaultName(commanderSlug, savedAt) : name.Trim(),
            CommanderSlug:  commanderSlug,
            TotalPriceEur:  total,
            CardCount:      cards.Count,
            SavedAt:        savedAt,
            Cards:          cards,
            Bracket:        bracket,
            Notes:          notes,
            BudgetEur:      budgetEur,
            CommanderName:  commanderName);

        var g = new VDS.RDF.Graph();
        var deckNode = g.CreateUriNode(new Uri(MtgVocab.SavedDeckUri(id)));

        Assert(g, deckNode, RdfSpecsHelper.RdfType, g.CreateUriNode(new Uri(MtgVocab.Class("Deck"))));
        Assert(g, deckNode, MtgVocab.Property("hasSource"), g.CreateLiteralNode("saved"));
        Assert(g, deckNode, MtgVocab.Property("hasName"), g.CreateLiteralNode(deck.Name));
        Assert(g, deckNode, MtgVocab.Property("hasCommander"),
            g.CreateUriNode(new Uri(MtgVocab.CommanderUri(commanderSlug))));
        Assert(g, deckNode, MtgVocab.Property("hasTotalPriceEur"), Dec(g, total));
        Assert(g, deckNode, MtgVocab.Property("hasCardCount"), Int(g, cards.Count));
        Assert(g, deckNode, MtgVocab.Property("hasSavedAt"), DateTime(g, savedAt));
        if (!string.IsNullOrWhiteSpace(notes))
            Assert(g, deckNode, MtgVocab.Property("hasNotes"), g.CreateLiteralNode(notes));
        if (budgetEur is decimal b)
            Assert(g, deckNode, MtgVocab.Property("hasBudgetEur"), Dec(g, b));
        // Persisted so the plain-text export still names the commander after a
        // restart, without re-resolving the slug against the Scryfall cache.
        if (!string.IsNullOrWhiteSpace(commanderName))
            Assert(g, deckNode, MtgVocab.Property("hasCommanderName"),
                g.CreateLiteralNode(commanderName));
        if (bracket is not null)
        {
            Assert(g, deckNode, MtgVocab.Property("hasBracketLevel"), Int(g, bracket.Level));
            Assert(g, deckNode, MtgVocab.Property("hasBracketLabel"), g.CreateLiteralNode(bracket.Label));
        }

        // Card membership. Basic lands repeat, and RDF has no multiset, so each
        // copy gets its own membership node carrying the ordinal — otherwise a
        // 37-basic manabase would collapse to one triple on reload.
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var slot = g.CreateUriNode(new Uri($"{MtgVocab.SavedDeckUri(id)}/slot/{i}"));
            Assert(g, deckNode, MtgVocab.Property("hasSlot"), slot);
            Assert(g, slot, MtgVocab.Property("hasOrdinal"), Int(g, i));
            Assert(g, slot, MtgVocab.Property("hasCardName"), g.CreateLiteralNode(card.Name));
            if (!string.IsNullOrEmpty(card.OracleId))
                Assert(g, slot, MtgVocab.Property("hasOracleId"), g.CreateLiteralNode(card.OracleId));
            if (card.PriceEur is decimal p)
                Assert(g, slot, MtgVocab.Property("hasPriceEur"), Dec(g, p));
            if (!string.IsNullOrWhiteSpace(card.TypeLine))
                Assert(g, slot, MtgVocab.Property("hasTypeLine"), g.CreateLiteralNode(card.TypeLine));
            if (!string.IsNullOrWhiteSpace(card.Category))
                Assert(g, slot, MtgVocab.Property("hasCategoryLabel"), g.CreateLiteralNode(card.Category));
            if (!string.IsNullOrWhiteSpace(card.ImageUrl))
                Assert(g, slot, MtgVocab.Property("hasImageUrl"), g.CreateLiteralNode(card.ImageUrl));
        }

        await repo.WriteAsync(g, GraphUri, cancellationToken).ConfigureAwait(false);
        return deck;
    }

    public async Task<IReadOnlyList<SavedDeckSummary>> ListAsync(
        string? commanderSlug = null,
        CancellationToken cancellationToken = default)
    {
        var commanderFilter = commanderSlug is null
            ? ""
            : $"    ?deck mtg:hasCommander <{MtgVocab.CommanderUri(commanderSlug)}> .";

        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>

SELECT ?deck ?name ?commander ?total ?count ?savedAt ?bracketLevel ?bracketLabel ?budget
WHERE {{
  GRAPH <{GraphUri}> {{
    ?deck mtg:hasSource     ""saved"" ;
          mtg:hasName       ?name ;
          mtg:hasCommander  ?commander ;
          mtg:hasCardCount  ?count ;
          mtg:hasSavedAt    ?savedAt .
{commanderFilter}
    OPTIONAL {{ ?deck mtg:hasTotalPriceEur ?total }}
    OPTIONAL {{ ?deck mtg:hasBracketLevel  ?bracketLevel }}
    OPTIONAL {{ ?deck mtg:hasBracketLabel  ?bracketLabel }}
    OPTIONAL {{ ?deck mtg:hasBudgetEur     ?budget }}
  }}
}}
ORDER BY DESC(?savedAt)";

        var rs = await repo.QueryAsync(sparql, cancellationToken).ConfigureAwait(false);
        var list = new List<SavedDeckSummary>(rs.Count);
        foreach (var row in rs)
        {
            var uri = (row["deck"] as IUriNode)?.Uri.ToString();
            if (uri is null) continue;
            list.Add(new SavedDeckSummary(
                Id:             IdFromUri(uri),
                Name:           Str(row, "name") ?? "(untitled)",
                CommanderSlug:  SlugFromCommanderUri(row["commander"] as IUriNode),
                TotalPriceEur:  Dec(row, "total") ?? 0m,
                CardCount:      (int)(Dec(row, "count") ?? 0m),
                SavedAt:        Date(row, "savedAt") ?? DateTimeOffset.MinValue,
                BracketLevel:   Dec(row, "bracketLevel") is decimal bl ? (int)bl : null,
                BracketLabel:   Str(row, "bracketLabel"),
                BudgetEur:      Dec(row, "budget")));
        }
        return list;
    }

    public async Task<SavedDeck?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var deckUri = MtgVocab.SavedDeckUri(id);
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>

SELECT ?name ?commander ?total ?count ?savedAt ?notes ?budget ?bracketLevel ?bracketLabel ?commanderName
       ?ordinal ?cardName ?oracleId ?price ?typeLine ?category ?imageUrl
WHERE {{
  GRAPH <{GraphUri}> {{
    <{deckUri}> mtg:hasName      ?name ;
                mtg:hasCommander ?commander ;
                mtg:hasCardCount ?count ;
                mtg:hasSavedAt   ?savedAt .
    OPTIONAL {{ <{deckUri}> mtg:hasTotalPriceEur ?total }}
    OPTIONAL {{ <{deckUri}> mtg:hasNotes         ?notes }}
    OPTIONAL {{ <{deckUri}> mtg:hasBudgetEur     ?budget }}
    OPTIONAL {{ <{deckUri}> mtg:hasBracketLevel  ?bracketLevel }}
    OPTIONAL {{ <{deckUri}> mtg:hasBracketLabel  ?bracketLabel }}
    OPTIONAL {{ <{deckUri}> mtg:hasCommanderName ?commanderName }}
    OPTIONAL {{
      <{deckUri}> mtg:hasSlot ?slot .
      ?slot mtg:hasOrdinal  ?ordinal ;
            mtg:hasCardName ?cardName .
      OPTIONAL {{ ?slot mtg:hasOracleId     ?oracleId }}
      OPTIONAL {{ ?slot mtg:hasPriceEur     ?price }}
      OPTIONAL {{ ?slot mtg:hasTypeLine     ?typeLine }}
      OPTIONAL {{ ?slot mtg:hasCategoryLabel ?category }}
      OPTIONAL {{ ?slot mtg:hasImageUrl     ?imageUrl }}
    }}
  }}
}}
ORDER BY ?ordinal";

        var rs = await repo.QueryAsync(sparql, cancellationToken).ConfigureAwait(false);
        if (rs.Count == 0) return null;

        var first = rs.First();
        var cards = new List<CardRecommendation>();
        foreach (var row in rs)
        {
            var cardName = Str(row, "cardName");
            if (cardName is null) continue;
            cards.Add(new CardRecommendation(
                OracleId:     Str(row, "oracleId") ?? "",
                Name:         cardName,
                Category:     Str(row, "category"),
                InclusionPct: null,
                SynergyScore: null,
                PriceEur:     Dec(row, "price"),
                ImageUrl:     Str(row, "imageUrl"),
                TypeLine:     Str(row, "typeLine")));
        }

        DeckBracket? bracket = null;
        if (Dec(first, "bracketLevel") is decimal lvl)
        {
            bracket = new DeckBracket(
                Level:             (int)lvl,
                Label:             Str(first, "bracketLabel") ?? "",
                GameChangerCount:  0,
                GameChangersFound: [],
                HasMassLandDenial: false,
                HasExtraTurns:     false,
                // The bracket was computed at save time; only the verdict is
                // persisted, so the detailed reasoning is not reconstructable.
                Reasons:           ["Recorded when the deck was saved."]);
        }

        return new SavedDeck(
            Id:            id,
            Name:          Str(first, "name") ?? "(untitled)",
            CommanderSlug: SlugFromCommanderUri(first["commander"] as IUriNode),
            TotalPriceEur: Dec(first, "total") ?? 0m,
            CardCount:     (int)(Dec(first, "count") ?? cards.Count),
            SavedAt:       Date(first, "savedAt") ?? DateTimeOffset.MinValue,
            Cards:         cards,
            Bracket:       bracket,
            Notes:         Str(first, "notes"),
            BudgetEur:     Dec(first, "budget"),
            CommanderName: Str(first, "commanderName"));
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(id, cancellationToken).ConfigureAwait(false) is null) return false;

        var deckUri = MtgVocab.SavedDeckUri(id);
        // Delete the deck node and every slot hanging off it. Slots are only
        // ever referenced by their own deck, so this leaves nothing orphaned.
        var update = $@"
PREFIX mtg: <{MtgVocab.Namespace}>

DELETE {{ GRAPH <{GraphUri}> {{ <{deckUri}> ?p ?o . ?slot ?sp ?so . }} }}
WHERE  {{ GRAPH <{GraphUri}> {{
           <{deckUri}> ?p ?o .
           OPTIONAL {{ <{deckUri}> mtg:hasSlot ?slot . ?slot ?sp ?so . }}
         }} }}";
        await repo.UpdateAsync(update, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string DefaultName(string slug, DateTimeOffset at)
        => $"{slug} — {at:yyyy-MM-dd HH:mm}";

    private static string IdFromUri(string uri)
    {
        var idx = uri.LastIndexOf('/');
        return idx >= 0 ? uri[(idx + 1)..] : uri;
    }

    private static string SlugFromCommanderUri(IUriNode? node)
    {
        var uri = node?.Uri.ToString() ?? "";
        var idx = uri.LastIndexOf('/');
        return idx >= 0 ? uri[(idx + 1)..] : "";
    }

    private static void Assert(IGraph g, INode subj, string predicate, INode obj)
        => g.Assert(subj, g.CreateUriNode(new Uri(predicate)), obj);

    private static ILiteralNode Int(IGraph g, int i)
        => g.CreateLiteralNode(i.ToString(CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger));

    private static ILiteralNode Dec(IGraph g, decimal d)
        => g.CreateLiteralNode(d.ToString(CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode DateTime(IGraph g, DateTimeOffset at)
        => g.CreateLiteralNode(at.ToString("o", CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDateTime));

    private static string? Str(VDS.RDF.Query.ISparqlResult row, string var)
        => row.HasBoundValue(var) && row[var] is ILiteralNode lit ? lit.Value : null;

    private static decimal? Dec(VDS.RDF.Query.ISparqlResult row, string var)
    {
        if (!row.HasBoundValue(var) || row[var] is not ILiteralNode lit) return null;
        return decimal.TryParse(lit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }

    private static DateTimeOffset? Date(VDS.RDF.Query.ISparqlResult row, string var)
    {
        var s = Str(row, var);
        return s is not null
            && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var d)
            ? d : null;
    }
}
