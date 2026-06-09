# Study Guide — RDF, OWL, SHACL, SPARQL (with this project)

A guided tour of the four semantic-web specs that hold this project together.
Each section answers three questions:

1. **What is it?** — the spec in one paragraph
2. **Where does it show up here?** — exact files, with line numbers when useful
3. **When does it run?** — the runtime moment that triggers it

At the end there's an [end-to-end walk-through](#walk-through-one-card-through-every-spec)
that follows a single card (Windfall) through all four specs.

> Open this side-by-side with the source. Suggested layout:
> Zed left pane = this file, right pane = the file being discussed.

---

## 1. RDF — triple data model (subject → predicate → object)

### What it is

RDF is a way of expressing facts as **three-part statements** called triples:

```
<subject>    <predicate>    <object>
```

- **Subject** — always a URI (or a blank node)
- **Predicate** — always a URI
- **Object** — a URI, a blank node, or a typed literal (`"Windfall"`, `4.88^^xsd:decimal`, `2026-03-28^^xsd:date`)

That's it. Every fact in the database is one of those. Tables, joins, foreign
keys — none of that exists. The database is a *set of triples*. Two datasets
that use the same URI for the same thing merge with **zero ETL**.

### Where it shows up

| File | What it does |
|---|---|
| [`src/MtgDeckEngine.Core/MtgVocab.cs`](../src/MtgDeckEngine.Core/MtgVocab.cs) | The vocabulary — every URI we mint follows the patterns declared here |
| [`src/MtgDeckEngine.Ingestion/Mappers/ScryfallToRdfMapper.cs`](../src/MtgDeckEngine.Ingestion/Mappers/ScryfallToRdfMapper.cs) | Scryfall JSON → triples |
| [`src/MtgDeckEngine.Ingestion/Mappers/EdhrecToRdfMapper.cs`](../src/MtgDeckEngine.Ingestion/Mappers/EdhrecToRdfMapper.cs) | EDHREC JSON → triples (commander-scoped) |
| [`src/MtgDeckEngine.Ingestion/Mappers/EdhTop16ToRdfMapper.cs`](../src/MtgDeckEngine.Ingestion/Mappers/EdhTop16ToRdfMapper.cs) | EDHTop16 GraphQL → triples |
| [`src/MtgDeckEngine.Ingestion/Mappers/TopDeckToRdfMapper.cs`](../src/MtgDeckEngine.Ingestion/Mappers/TopDeckToRdfMapper.cs) | TopDeck.gg REST → triples |

### When it runs

Triples are minted **every time an ingestor processes a record**. The graph
object (`VDS.RDF.Graph`) lives in memory; it gets flushed to the triplestore
via `IGraphRepository.WriteAsync(graph, namedGraphUri, ct)`.

### How it looks in C#

Open `ScryfallToRdfMapper.cs`. The core pattern is:

```csharp
var cardNode = g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId)));
var rdfType  = g.CreateUriNode(new Uri(RdfSpecsHelper.RdfType));
var cardClass = g.CreateUriNode(new Uri(MtgVocab.Class("Card")));

// triple #1 — Windfall is a Card
g.Assert(cardNode, rdfType, cardClass);

// triple #2 — Windfall has the name "Windfall"
g.Assert(cardNode,
    g.CreateUriNode(new Uri(MtgVocab.Property("hasName"))),
    g.CreateLiteralNode(card.Name));

// triple #3 — Windfall costs €4.88
g.Assert(cardNode,
    g.CreateUriNode(new Uri(MtgVocab.Property("hasPriceEur"))),
    g.CreateLiteralNode("4.88", new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal)));
```

`g.Assert(s, p, o)` is *the* RDF primitive. Three URIs / literals go in, one
triple comes out. **Every other operation in this codebase is a wrapper over
this call.**

### How it looks in Turtle (the file format)

The same three triples as Turtle (what you'd write by hand):

```turtle
@prefix mtg:  <http://example.org/mtg#> .
@prefix xsd:  <http://www.w3.org/2001/XMLSchema#> .

mtg:card/08becc07-…  a  mtg:Card ;
                     mtg:hasName     "Windfall" ;
                     mtg:hasPriceEur "4.88"^^xsd:decimal .
```

`a` is shorthand for `rdf:type`. The semicolon means "same subject, different
predicate/object". Turtle is just a compact way of writing triples — the
underlying model is identical.

### Why URIs matter

A subject like `mtg:card/08becc07-28bc-4a2f-a6b0-28a2998d2f50` (the Scryfall
oracle ID for Windfall) is **globally unique**. When EDHTop16 returns the same
oracle ID in a maindeck array, our mapper mints the exact same URI — so
EDHREC's "78% inclusion" fact and EDHTop16's "appeared in this tournament"
fact attach to the *same node*. No join needed. That's the entire reason
this project can fuse four data sources without an ETL pipeline.

> Look at `MtgVocab.CardUri(oracleId)` in
> [`MtgVocab.cs`](../src/MtgDeckEngine.Core/MtgVocab.cs). One line, but it's
> the linchpin of the whole system.

---

## 2. OWL — ontology with inference (open-world)

### What it is

OWL (Web Ontology Language) is a vocabulary for **describing the schema** of
your RDF data. It defines:

- **Classes** — like SQL tables, but a node can be in many at once
- **Properties** — like columns, but with `rdfs:domain` (what subjects can use it)
  and `rdfs:range` (what objects are allowed)
- **Hierarchies** — `rdfs:subClassOf`, `rdfs:subPropertyOf`
- **Identity** — `owl:sameAs`, `owl:equivalentClass`
- **Inference rules** — a SPARQL/OWL reasoner can derive new triples that
  aren't explicitly stored

The killer detail: **open-world semantics**. The absence of a triple does
*not* mean its negation. If we don't have a `mtg:hasPriceEur` for some card,
that does **not** mean the card is free — it means we don't know.
Contrast with SQL, where `WHERE price IS NULL` is true with confidence.

### Where it shows up

| File | What it does |
|---|---|
| [`src/MtgDeckEngine.Ontology/mtg-ontology.ttl`](../src/MtgDeckEngine.Ontology/mtg-ontology.ttl) | The TBox — class + property definitions |
| [`src/MtgDeckEngine.Ontology/OntologyResources.cs`](../src/MtgDeckEngine.Ontology/OntologyResources.cs) | Loads the .ttl as an embedded resource at runtime |
| [`src/MtgDeckEngine.Ingestion/Workers/StartupIngestionWorker.cs`](../src/MtgDeckEngine.Ingestion/Workers/StartupIngestionWorker.cs) | Asserts the TBox into the triplestore at boot |

### When it runs

The ontology .ttl gets loaded **once at startup**, before any ingestion:

```csharp
// StartupIngestionWorker.ExecuteAsync, ~line 30
logger.LogInformation("Loading TBox + SHACL shapes into the triplestore");
await repo.LoadTurtleAsync(
    OntologyResources.Ontology,    // the embedded .ttl as a string
    namedGraphUri: null,           // default graph
    ct).ConfigureAwait(false);
```

After that, every triple we ingest gets typed against this schema. SPARQL
queries can use the class hierarchy transparently.

### How it looks

Open `mtg-ontology.ttl`. Three excerpts:

#### Classes — the "TBox"

```turtle
mtg:Card            a owl:Class .                              # generic card
mtg:Commander       a owl:Class ; rdfs:subClassOf mtg:Card .   # commanders are also cards
mtg:Deck            a owl:Class .
mtg:Tournament      a owl:Class .
mtg:TournamentEntry a owl:Class .                              # one player's result in one tournament
```

The `rdfs:subClassOf` on `mtg:Commander` is the inference hook:
**Xyris is asserted as a Commander, but a reasoner can derive that Xyris is
also a Card.** Any query for "all cards" picks up Xyris for free.

#### Properties with domain and range

```turtle
mtg:hasName        a owl:DatatypeProperty ; rdfs:range xsd:string .
mtg:hasOracleId    a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:string .
mtg:hasPriceEur    a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:decimal .
mtg:containsCard   a owl:ObjectProperty   ; rdfs:domain mtg:Deck ; rdfs:range mtg:Card .
mtg:hasCommander   a owl:ObjectProperty   ; rdfs:domain mtg:Deck ; rdfs:range mtg:Commander .
```

`DatatypeProperty` = the object is a literal (`"Windfall"`, `4.88`).
`ObjectProperty` = the object is another URI (a Card, a Commander, etc.).

#### The open-world subtlety in this codebase

In Phase 3 we needed `mtg:hasFormat` on both Tournaments and Decks. Look at
[line 84](../src/MtgDeckEngine.Ontology/mtg-ontology.ttl):

```turtle
# Applies to Tournament and Deck (multi-format: COMMANDER, MODERN, LEGACY, ...).
mtg:hasFormat   a owl:DatatypeProperty ; rdfs:range xsd:string .
```

Notice **no `rdfs:domain`**. That's intentional — the property is general
enough to apply to multiple classes. Open-world semantics let us do this:
a reasoner won't reject `?deck mtg:hasFormat "MODERN"` just because we didn't
explicitly say "decks can have a format". If the data fits the *range*
(it's a string), the triple is valid.

In closed-world SQL terms: imagine adding a column to two tables without
running `ALTER TABLE`. Impossible. In OWL it's just one line.

### Inference — what a reasoner would derive

If we had a reasoner enabled (we don't — dotNetRDF supports it but we keep
inference off for performance), these facts would be auto-derived:

```
Asserted:  mtg:card/xyris  a  mtg:Commander
Asserted:  mtg:Commander   rdfs:subClassOf  mtg:Card
─────────────────────────────────────────────────────
Inferred:  mtg:card/xyris  a  mtg:Card    ← derived by RDFS reasoning
```

For now, our queries that ask for `?c a mtg:Card` won't return Xyris unless
we *also* explicitly assert `mtg:card/xyris a mtg:Card` (which the ingestor
does — see `ScryfallToRdfMapper.AssertCard`). This is a "manual inference"
trade-off: cheaper queries, more triples stored.

---

## 3. SHACL — shape validation (closed-world)

### What it is

SHACL (Shapes Constraint Language) describes **what must be true** for data
to be considered valid. It's the closed-world counterpart to OWL's
open-world declarations.

- OWL: "A `mtg:hasPriceEur` is a string." → if absent, just unknown.
- SHACL: "A `mtg:Card` must have at least one `mtg:hasOracleId`." → if absent,
  validation fails with a specific error message.

Think of it as `CHECK CONSTRAINT` for graphs.

### Where it shows up

| File | What it does |
|---|---|
| [`src/MtgDeckEngine.Ontology/mtg-shapes.ttl`](../src/MtgDeckEngine.Ontology/mtg-shapes.ttl) | The shapes — declarative constraints |
| [`src/MtgDeckEngine.Graph/Validation/ShaclValidator.cs`](../src/MtgDeckEngine.Graph/Validation/ShaclValidator.cs) | Loads shapes + runs `ShapesGraph.Validate(dataGraph)` |
| [`src/MtgDeckEngine.Graph/Validation/ShaclValidationException.cs`](../src/MtgDeckEngine.Graph/Validation/ShaclValidationException.cs) | Thrown if a graph fails validation |

### When it runs

**Currently: not in the ingestion path.** This is honest design intent vs
current state — the `ShaclValidator` is registered in DI but no ingestor
calls it before `WriteAsync`. To wire it in, we'd add this to (for example)
`EdhTop16Ingestor.IngestCommanderAsync`:

```csharp
shaclValidator.Validate(entryGraph);              // throws on violation
await repo.WriteAsync(entryGraph, null, ct);      // only writes if validation passed
```

That's the intended Phase 6 hardening step.

### How it looks

#### A simple shape

```turtle
mtg:CardShape a sh:NodeShape ;
    sh:targetClass mtg:Card ;
    sh:property [
        sh:path mtg:hasOracleId ;
        sh:minCount 1 ;
        sh:datatype xsd:string ;
        sh:message "Card must have a Scryfall oracle ID."
    ] ;
    sh:property [
        sh:path mtg:hasName ;
        sh:minCount 1 ;
        sh:datatype xsd:string ;
        sh:message "Card must have a name."
    ] .
```

In English: "For every node typed as `mtg:Card`, it MUST have at least one
`mtg:hasOracleId` (a string) and at least one `mtg:hasName` (a string).
If not, fail with these messages."

#### Format-specific shapes (Phase 3 refactor)

Originally we had:

```turtle
mtg:DeckShape ...
    sh:property [ sh:path mtg:containsCard ; sh:minCount 99 ; sh:maxCount 100 ] ;  # ← 99-100 cards
    sh:property [ sh:path mtg:hasCommander ; sh:minCount 1 ; sh:maxCount 1 ] .
```

That broke when Modern decks (60+ cards, no commander) showed up. **Phase 3
refactored** into focused shapes — the general one stays loose, format-specific
ones layer on as needed (see the comment block in
[`mtg-shapes.ttl`](../src/MtgDeckEngine.Ontology/mtg-shapes.ttl)):

```turtle
mtg:DeckShape a sh:NodeShape ;             # any deck — minimal contract
    sh:targetClass mtg:Deck ;
    sh:property [
        sh:path mtg:hasSource ;
        sh:minCount 1 ;
        sh:message "A deck must declare its source."
    ] .

# Commander- and Constructed-specific shapes documented in the .ttl,
# enabled by the validator only when relevant.
```

This is a Phase 3 lesson worth keeping: **SHACL shapes are programs that
should follow single-responsibility**. Don't make one shape know about
Modern *and* EDH *and* Pioneer.

#### The validator

```csharp
// ShaclValidator.cs — ~15 lines
public sealed class ShaclValidator
{
    private readonly ShapesGraph _shapes;

    public ShaclValidator() : this(OntologyResources.Shapes) { }

    public ShaclValidator(string shapesTurtle)
    {
        var g = new RdfGraph();
        g.LoadFromString(shapesTurtle, new TurtleParser());
        _shapes = new ShapesGraph(g);
    }

    public void Validate(IGraph dataGraph)
    {
        var report = _shapes.Validate(dataGraph);
        if (!report.Conforms)
            throw new ShaclValidationException(report);
    }
}
```

`Report.Results` would contain a list of every failing node, the failing
predicate, and the message from the shape. This is exactly the data you'd
log or surface in an API error response.

### Open-world (OWL) vs closed-world (SHACL) — a worked example

A card is asserted *without* an oracle ID:

```turtle
mtg:card/mystery  a  mtg:Card ;
                  mtg:hasName  "???" .
```

- **OWL view:** no problem. We just don't know the oracle ID yet. Maybe
  another data source will provide it later. The graph is consistent.
- **SHACL view:** `CardShape` requires `sh:minCount 1` on `mtg:hasOracleId`.
  Validation **fails**. The data is not allowed in.

Both views are useful. OWL is for *reasoning about* incomplete data; SHACL
is for *gating* what enters the store.

---

## 4. SPARQL — SQL for graphs

### What it is

SPARQL is to RDF what SQL is to relational data. You write *graph patterns*
(triples with variables) and the engine finds every binding of those
variables in the dataset.

SQL-ish features map across pretty directly:

| SQL | SPARQL |
|---|---|
| `SELECT col FROM ...` | `SELECT ?var WHERE { … }` |
| `JOIN ... ON ...` | Repeated subject (`?card mtg:hasName ?n ; mtg:hasPriceEur ?p`) |
| `LEFT JOIN` | `OPTIONAL { … }` |
| `WHERE` | `FILTER ( … )` |
| `GROUP BY` / `HAVING` | `GROUP BY ?x` / `HAVING (?count > 5)` |
| `ORDER BY ... LIMIT` | `ORDER BY DESC(?x) LIMIT 25` |
| schemas / databases | named graphs (`GRAPH ?g { … }`) |
| `EXISTS (SELECT 1 ...)` | nested `EXISTS { … }` |
| `UNION` | `UNION` (same idea) |

Plus a handful of things SQL doesn't have:

- **Property paths** (`?a mtg:hasCommander/mtg:hasName ?n`) — follow chains
- **`a` shorthand** for `rdf:type`
- **`OPTIONAL` doesn't collapse to NULL** — the variable is *unbound*, and
  you check that with `BOUND(?x)`

### Where it shows up

| File | What it does |
|---|---|
| [`queries/*.sparql`](../queries/) | 18 versioned, runnable queries |
| [`src/MtgDeckEngine.Graph/DeckRecommendationService.cs`](../src/MtgDeckEngine.Graph/DeckRecommendationService.cs) | Builds the recommendation query dynamically (uses `StringBuilder`) |
| [`src/MtgDeckEngine.Graph/FormatService.cs`](../src/MtgDeckEngine.Graph/FormatService.cs) | Format-level queries (Phase 4a) |
| [`src/MtgDeckEngine.Graph/Repositories/FusekiGraphRepository.cs`](../src/MtgDeckEngine.Graph/Repositories/FusekiGraphRepository.cs) | Sends SPARQL over HTTP to Fuseki/Neptune |
| [`src/MtgDeckEngine.Graph/Repositories/InMemoryGraphRepository.cs`](../src/MtgDeckEngine.Graph/Repositories/InMemoryGraphRepository.cs) | Runs SPARQL via dotNetRDF's Leviathan engine (used in tests) |
| [`bin/sparql`](../bin/sparql) | The runner script — POSTs a `.sparql` file to the endpoint |

### When it runs

Every API request (`GET /api/commanders/{slug}/recommendations`,
`/meta`, `/staples`, etc.) builds a SPARQL string and dispatches it through
`IGraphRepository.QueryAsync(sparql, ct)`. The repository picks the transport
(HTTP for Fuseki/Neptune, in-process for tests) and returns the
`SparqlResultSet`.

### Three real queries from this project

#### 1. The DoD query (Phase 2)

["Cards in Top-4 Xyris decks under €5"](../queries/14-top-cards-in-top4.sparql):

```sparql
PREFIX mtg: <http://example.org/mtg#>

SELECT ?name ?priceEur (COUNT(DISTINCT ?deck) AS ?numDecks) WHERE {
  ?entry mtg:inTournament ?t ;
         mtg:hasPlacement ?placement ;
         mtg:hasDeck       ?deck .
  ?deck  mtg:hasCommander  mtg:commander/xyris-the-writhing-storm ;
         mtg:containsCard  ?card .
  ?card  mtg:hasName       ?name .
  OPTIONAL { ?card mtg:hasPriceEur ?priceEur }
  FILTER (?placement <= 4)
  FILTER (!BOUND(?priceEur) || ?priceEur <= 5.0)
}
GROUP BY ?name ?priceEur
ORDER BY DESC(?numDecks) ?priceEur
LIMIT 25
```

Read top-down:

1. Find tournament entries with a placement and a deck.
2. The deck has Xyris as commander and contains some card.
3. The card has a name.
4. *Maybe* the card has a price.
5. Keep only placements ≤ 4 and (price ≤ €5 or unpriced).
6. Group by card and count distinct decks.
7. Sort by deck count, take 25.

Things to notice:

- `?card mtg:hasName ?name` is a **join**. Same `?card` variable links it
  back to the deck triple. Done.
- `OPTIONAL` makes the price an outer join. If a card has no price, it still
  matches — `?priceEur` is unbound.
- `FILTER (!BOUND(?priceEur) || ?priceEur <= 5.0)` is the
  `(price IS NULL OR price <= 5)` equivalent.
- We never wrote a `WHERE deck.commander_id = ?`. The graph pattern
  expresses the join inline.

#### 2. Mixing default graph and named graphs

[Popularity vs performance gap](../queries/15-popularity-vs-performance.sparql):

```sparql
PREFIX mtg: <http://example.org/mtg#>

SELECT ?name ?inc (COUNT(?topCutDeck) AS ?topCutAppearances) WHERE {

  # EDHREC inclusion lives in a commander-scoped NAMED graph
  GRAPH <http://example.org/mtg#context/xyris-the-writhing-storm> {
    ?card mtg:hasInclusionPct ?inc .
  }

  # Card name lives in the DEFAULT graph (global card facts)
  ?card mtg:hasName ?name .

  FILTER (?inc > 40.0)

  # Tournament data also lives in the default graph
  OPTIONAL {
    ?entry mtg:hasPlacement ?p ;
           mtg:hasDeck       ?topCutDeck .
    ?topCutDeck mtg:hasCommander mtg:commander/xyris-the-writhing-storm ;
                mtg:containsCard ?card .
    FILTER (?p <= 8)
  }
}
GROUP BY ?name ?inc
ORDER BY DESC(?inc) ?topCutAppearances
LIMIT 25
```

This is the killer use case for named graphs. EDHREC's "78% inclusion" is
**only true in the context of Xyris** — so it lives in
`mtg:context/xyris-the-writhing-storm`. Global facts (card name, price)
live in the default graph. **The same `?card` variable connects them.**

#### 3. SPARQL built in C#

Open [`DeckRecommendationService.cs`](../src/MtgDeckEngine.Graph/DeckRecommendationService.cs).
The `BuildQuery` method assembles SPARQL with a `StringBuilder` based on the
caller's `RecommendationFilter`:

```csharp
var sb = new StringBuilder();
sb.AppendLine($"PREFIX mtg:  <{MtgVocab.Namespace}>");
sb.AppendLine("PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>");
sb.AppendLine("PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>");
sb.AppendLine();
sb.AppendLine("SELECT ?oracleId ?name ?categoryLabel ?inclusion ?synergy ?priceEur ?topCutCount WHERE {");

// Pull from EDHREC named graph
if (tourMode) sb.AppendLine("  OPTIONAL {");
sb.AppendLine($"  GRAPH <{ctx}> {{");
sb.AppendLine("    ?card mtg:hasInclusionPct ?inclusion .");
//   …more graph pattern…
sb.AppendLine("  }");
if (tourMode) sb.AppendLine("  }");

// Global card facts
sb.AppendLine("  ?card mtg:hasOracleId ?oracleId ;");
sb.AppendLine("        mtg:hasName     ?name ;");
sb.AppendLine("        mtg:hasTypeLine ?typeLine .");
// …filters…
```

The result is a SPARQL string that gets dispatched as a single SPARQL 1.1
HTTP request. Same string works against Fuseki locally and Neptune in prod.

### Running queries from the command line

```bash
# Saved file
./bin/sparql queries/14-top-cards-in-top4.sparql

# Inline (against any SPARQL 1.1 endpoint)
curl -s 'http://localhost:3030/mtg/sparql' \
  --data-urlencode 'query=SELECT (COUNT(*) AS ?n) WHERE { ?s ?p ?o }' \
  -H 'Accept: application/sparql-results+json' | jq
```

The runner detects `CONSTRUCT`/`DESCRIBE` queries and switches the `Accept`
header to `text/turtle`; otherwise it asks for JSON and pipes through `jq`.

---

## Walk-through — one card through every spec

Let's follow **Windfall** through Phase 2 ingestion to see all four specs
running in sequence.

### Step 1 — RDF: the URI is minted

In `EdhTop16Ingestor.IngestCommanderAsync` we get this back from EDHTop16:

```json
{
  "name": "Windfall",
  "oracleId": "08becc07-28bc-4a2f-a6b0-28a2998d2f50"
}
```

The mapper [`EdhTop16ToRdfMapper.AssertEntry`](../src/MtgDeckEngine.Ingestion/Mappers/EdhTop16ToRdfMapper.cs) does:

```csharp
var cardNode = g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId)));
//        ─────────────────────────────────────────────────────────────
//        produces http://example.org/mtg#card/08becc07-28bc-4a2f-…
g.Assert(deck, containsCardProp, cardNode);
```

That URI is **identical** to the one minted by `ScryfallToRdfMapper` when it
ingested the same oracle ID earlier. Same URI, automatic join.

### Step 2 — OWL: the schema knows what Windfall is

`OntologyResources.Ontology` has already been loaded:

```turtle
mtg:Card           a owl:Class .
mtg:hasName        a owl:DatatypeProperty ; rdfs:range xsd:string .
mtg:containsCard   a owl:ObjectProperty   ; rdfs:domain mtg:Deck ; rdfs:range mtg:Card .
```

So when we assert `?deck mtg:containsCard mtg:card/08becc07-…`, the schema
says: `?deck` must be a Deck (domain), and the object must be a Card (range).
A reasoner could **derive** `mtg:card/08becc07-… a mtg:Card` from that range
constraint, even if we never assert it explicitly. (We *do* assert it from
Scryfall, so this is belt-and-braces. But it would still be correct without
that assertion.)

### Step 3 — SHACL: validation gate (intent)

Before writing, we'd call:

```csharp
shaclValidator.Validate(entryGraph);
```

Against `CardShape`, every card node in the graph must have ≥1 `hasOracleId`
literal and ≥1 `hasName` literal. Windfall passes (we have both). A
half-ingested record (no oracle ID) would throw `ShaclValidationException`
with a message pinpointing the bad node.

> Today the validator exists but the ingestors don't call it; wiring it in
> is the Phase 6 ingestion-hardening step.

### Step 4 — SPARQL: answering the question

Later, a user hits:

```
GET /api/commanders/xyris-the-writhing-storm/recommendations
        ?source=Tournament&maxPlacement=4&maxPriceEur=5&limit=15
```

`DeckRecommendationService.GetRecommendationsAsync` builds (essentially) the
DoD query shown above and runs it. Windfall comes back as one of the rows
because:

- It was asserted into the global graph by `ScryfallToRdfMapper`
  (it has a name, a price, a type line, an oracle id).
- It was linked into a Xyris tournament deck by `EdhTop16ToRdfMapper`
  (the `containsCard` triple).
- The query's filter (`?placement <= 4 AND ?priceEur <= 5`) accepts it.
- It groups by name + price, counts distinct decks containing it.

All four specs participated:

- **RDF** — `g.Assert` is what put the data in.
- **OWL** — the schema's class/property declarations made the patterns
  syntactically valid.
- **SHACL** — the structural contract for what a Card / Deck / Entry
  must contain.
- **SPARQL** — the query that answered the user's question.

---

## Suggested exercises

To make this stick, try these in the Fuseki UI (http://localhost:3030):

1. **Count by class.** "How many cards, commanders, decks, tournaments do
   we have?"
   ```sparql
   PREFIX mtg: <http://example.org/mtg#>
   SELECT ?cls (COUNT(?s) AS ?n) WHERE { ?s a ?cls }
   GROUP BY ?cls ORDER BY DESC(?n)
   ```

2. **Property-path traversal.** "Which tournaments did Xyris-piloted decks
   appear in?"
   ```sparql
   PREFIX mtg: <http://example.org/mtg#>
   SELECT DISTINCT ?tName ?date WHERE {
     ?e mtg:hasDeck/mtg:hasCommander mtg:commander/xyris-the-writhing-storm ;
        mtg:inTournament ?t .
     ?t mtg:hasName ?tName ; mtg:hasDate ?date .
   } ORDER BY DESC(?date)
   ```
   Note `?e mtg:hasDeck/mtg:hasCommander …` — that's a property *path*,
   collapsing two triple patterns into one.

3. **Closed-world check by hand.** Find a card asserted as a Card but
   missing a name:
   ```sparql
   PREFIX mtg: <http://example.org/mtg#>
   SELECT ?c WHERE {
     ?c a mtg:Card .
     FILTER NOT EXISTS { ?c mtg:hasName ?n }
   }
   ```
   Expect 0 rows; if you get any, that's a SHACL violation in the wild.

4. **Wire SHACL into ingestion.** As a real exercise: pick one ingestor,
   inject `ShaclValidator`, call `Validate(graph)` before `WriteAsync`.
   Run ingestion against a deliberately broken record (delete `hasName`
   from a test fixture) and confirm the exception is thrown.

---

## Further reading (focused)

- **RDF 1.1 primer** — https://www.w3.org/TR/rdf11-primer/
- **OWL 2 primer** — https://www.w3.org/TR/owl2-primer/
- **SHACL spec** — https://www.w3.org/TR/shacl/
- **SPARQL 1.1 spec** — https://www.w3.org/TR/sparql11-query/
- **dotNetRDF docs** — https://dotnetrdf.org/docs/

The W3C primers are dense but rigorous; read them as references, not
tutorials. The fastest tutorial loop is the one you already have:
write a SPARQL query → run it against your own data → see what comes back.
