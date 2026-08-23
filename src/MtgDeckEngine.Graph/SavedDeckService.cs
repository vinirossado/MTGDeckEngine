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
            // The reasoning is what makes a bracket actionable — "Bracket 3"
            // alone does not tell you which cards put it there, and it cannot be
            // recomputed on read without re-querying Commander Spellbook (whose
            // verdict may since have changed). Persist the whole verdict.
            Assert(g, deckNode, MtgVocab.Property("hasBracketIsEstimate"), Bool(g, bracket.IsEstimate));
            Assert(g, deckNode, MtgVocab.Property("hasMassLandDenial"), Bool(g, bracket.HasMassLandDenial));
            Assert(g, deckNode, MtgVocab.Property("hasExtraTurns"), Bool(g, bracket.HasExtraTurns));
            foreach (var gc in bracket.GameChangersFound)
                Assert(g, deckNode, MtgVocab.Property("hasGameChanger"), g.CreateLiteralNode(gc));
            // Ordinal-tagged so the reasons come back in the order they were
            // written; a plain repeated property has no order in RDF.
            for (var i = 0; i < bracket.Reasons.Count; i++)
            {
                var reason = g.CreateUriNode(new Uri($"{MtgVocab.SavedDeckUri(id)}/reason/{i}"));
                Assert(g, deckNode, MtgVocab.Property("hasBracketReason"), reason);
                Assert(g, reason, MtgVocab.Property("hasOrdinal"), Int(g, i));
                Assert(g, reason, MtgVocab.Property("hasText"), g.CreateLiteralNode(bracket.Reasons[i]));
            }
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
       ?bracketIsEstimate ?bracketMld ?bracketExtraTurns
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
    OPTIONAL {{ <{deckUri}> mtg:hasBracketIsEstimate ?bracketIsEstimate }}
    OPTIONAL {{ <{deckUri}> mtg:hasMassLandDenial    ?bracketMld }}
    OPTIONAL {{ <{deckUri}> mtg:hasExtraTurns        ?bracketExtraTurns }}
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
            // Reasons and Game Changers are fetched separately rather than
            // joined above: the main query already fans out one row per card
            // slot, and joining two more repeated properties would multiply
            // 99 rows by every reason by every Game Changer.
            var (reasons, gameChangers) =
                await GetBracketDetailAsync(id, cancellationToken).ConfigureAwait(false);
            bracket = new DeckBracket(
                Level:             (int)lvl,
                Label:             Str(first, "bracketLabel") ?? "",
                GameChangerCount:  gameChangers.Count,
                GameChangersFound: gameChangers,
                HasMassLandDenial: Bool(first, "bracketMld"),
                HasExtraTurns:     Bool(first, "bracketExtraTurns"),
                Reasons:           reasons.Count > 0
                    ? reasons
                    : ["Recorded when the deck was saved."],
                IsEstimate:        Bool(first, "bracketIsEstimate"));
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

    /// <summary>
    /// Bracket reasoning for one deck: the ordered explanation lines and the
    /// Game Changers that were found. Kept out of the main deck query so the
    /// per-card rows are not multiplied by them.
    /// </summary>
    private async Task<(IReadOnlyList<string> Reasons, IReadOnlyList<string> GameChangers)>
        GetBracketDetailAsync(string id, CancellationToken cancellationToken)
    {
        var deckUri = MtgVocab.SavedDeckUri(id);
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>

SELECT ?ordinal ?text ?gameChanger WHERE {{
  GRAPH <{GraphUri}> {{
    {{
      <{deckUri}> mtg:hasBracketReason ?reason .
      ?reason mtg:hasOrdinal ?ordinal ; mtg:hasText ?text .
    }} UNION {{
      <{deckUri}> mtg:hasGameChanger ?gameChanger .
    }}
  }}
}}
ORDER BY ?ordinal";

        var rs = await repo.QueryAsync(sparql, cancellationToken).ConfigureAwait(false);
        var reasons = new List<string>();
        var gameChangers = new List<string>();
        foreach (var row in rs)
        {
            if (Str(row, "text") is { } text) reasons.Add(text);
            if (Str(row, "gameChanger") is { } gc) gameChangers.Add(gc);
        }
        gameChangers.Sort(StringComparer.OrdinalIgnoreCase);
        return (reasons, gameChangers);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(id, cancellationToken).ConfigureAwait(false) is null) return false;

        var deckUri = MtgVocab.SavedDeckUri(id);
        // Three independent statements rather than one pattern with two
        // OPTIONALs. That earlier form cross-multiplied the deck's own triples
        // by every slot triple by every reason triple — ~5,000 solutions for a
        // 99-card deck — and the store silently dropped the tail, leaving the
        // last slots and the deck's own hasBudgetEur behind. Each statement
        // here is linear in the thing it deletes.
        //
        // Order matters: the slot and reason nodes are only reachable through
        // the deck node, so they have to go before it does.
        var update = $@"
PREFIX mtg: <{MtgVocab.Namespace}>

DELETE {{ GRAPH <{GraphUri}> {{ ?slot ?p ?o }} }}
WHERE  {{ GRAPH <{GraphUri}> {{ <{deckUri}> mtg:hasSlot ?slot . ?slot ?p ?o }} }} ;

DELETE {{ GRAPH <{GraphUri}> {{ ?reason ?p ?o }} }}
WHERE  {{ GRAPH <{GraphUri}> {{ <{deckUri}> mtg:hasBracketReason ?reason . ?reason ?p ?o }} }} ;

DELETE {{ GRAPH <{GraphUri}> {{ <{deckUri}> ?p ?o }} }}
WHERE  {{ GRAPH <{GraphUri}> {{ <{deckUri}> ?p ?o }} }}";
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
        => g.CreateLiteralNode(RdfLiterals.Decimal(d),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode Bool(IGraph g, bool b)
        => g.CreateLiteralNode(b ? "true" : "false",
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeBoolean));

    private static ILiteralNode DateTime(IGraph g, DateTimeOffset at)
        => g.CreateLiteralNode(at.ToString("o", CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDateTime));

    private static string? Str(VDS.RDF.Query.ISparqlResult row, string var)
        => row.HasBoundValue(var) && row[var] is ILiteralNode lit ? lit.Value : null;

    private static bool Bool(VDS.RDF.Query.ISparqlResult row, string var)
        => Str(row, var) is { } v && bool.TryParse(v, out var b) && b;

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
