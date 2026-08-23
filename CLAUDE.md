# MTG Deck Intelligence Engine — CLAUDE.md

## Project Overview

A .NET 10 knowledge graph engine that aggregates Magic: The Gathering deck data from
multiple sources (EDHREC, EDHTop16, Spicerack, Scryfall, Moxfield), stores it as RDF
triples in a triplestore, and exposes a query API for deck recommendations, card
frequency analysis, budget filtering, and competitive performance insights.

**Core problem it solves:** EDHREC shows *popularity*, EDHTop16 shows *tournament
performance*. These are different signals. This engine fuses them into a single knowledge
graph so you can ask: "What are the most tournament-winning cards in Xyris decks that
cost under €5?" — a query no single source can answer alone.

**Primary use case:** Commander (EDH) format, initially scoped to the commander
Xyris, the Writhing Storm (Temur wheels/tokens archetype).

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core 10 |
| RDF library | dotNetRDF 3.x (`dotnet add package dotNetRdf`) |
| SHACL validation | `dotnet add package dotNetRdf.Shacl` |
| Triplestore client | `dotnet add package dotNetRdf.Client` |
| Local triplestore | Apache Jena Fuseki (Docker) |
| Production triplestore | Amazon Neptune (AWS — RDF/SPARQL mode) |
| Background workers | `IHostedService` + `BackgroundService` |
| HTTP client | `HttpClient` with `IHttpClientFactory` |
| Serialisation | `System.Text.Json` |
| Tests | xUnit + Testcontainers (Fuseki container) |

### Why dotNetRDF
- THE canonical .NET RDF library, actively maintained (v3.4.1, Oct 2025)
- In-memory SPARQL engine (Leviathan) for unit tests without a running store
- `SparqlQueryClient` connects to any SPARQL 1.1 HTTP endpoint (Fuseki, Neptune)
- `ShaclProcessor` validates graphs against SHACL shapes before write

---

## Project Structure

```
MtgDeckEngine/
├── CLAUDE.md                          ← you are here
├── docker-compose.yml                 ← Fuseki + the API
├── docker/
│   └── fuseki/
│       └── config.ttl                 ← Fuseki dataset config
├── src/
│   ├── MtgDeckEngine.Api/             ← ASP.NET Core 8 Web API
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── CommandersController.cs
│   │   │   └── CardsController.cs
│   │   └── appsettings.json
│   │
│   ├── MtgDeckEngine.Core/            ← Domain interfaces + models
│   │   ├── Interfaces/
│   │   │   ├── IIngestorService.cs
│   │   │   ├── IGraphRepository.cs
│   │   │   └── IDeckRecommendationService.cs
│   │   └── Models/
│   │       ├── CardDto.cs
│   │       ├── DeckDto.cs
│   │       └── TournamentEntryDto.cs
│   │
│   ├── MtgDeckEngine.Ontology/        ← RDF/OWL ontology + SHACL shapes
│   │   ├── mtg-ontology.ttl           ← OWL class definitions
│   │   ├── mtg-shapes.ttl             ← SHACL validation shapes
│   │   └── bootstrap/
│   │       └── seed-xyris.ttl         ← small seed dataset for dev/tests
│   │
│   ├── MtgDeckEngine.Ingestion/       ← ETL background workers
│   │   ├── Workers/
│   │   │   ├── ScryfallIngestorWorker.cs
│   │   │   ├── EdhrecIngestorWorker.cs
│   │   │   ├── EdhTop16IngestorWorker.cs
│   │   │   └── SpicerackIngestorWorker.cs
│   │   ├── Mappers/
│   │   │   ├── ScryfallToRdfMapper.cs
│   │   │   ├── EdhrecToRdfMapper.cs
│   │   │   └── TournamentToRdfMapper.cs
│   │   └── Http/
│   │       ├── ScryfallClient.cs
│   │       ├── EdhrecClient.cs
│   │       ├── EdhTop16Client.cs
│   │       └── SpicerackClient.cs
│   │
│   └── MtgDeckEngine.Graph/           ← dotNetRDF wrappers + SPARQL
│       ├── FusekiGraphRepository.cs
│       ├── InMemoryGraphRepository.cs ← for tests
│       ├── ShaclValidator.cs
│       └── Queries/
│           ├── CardFrequencyQuery.sparql
│           ├── BudgetFilterQuery.sparql
│           ├── TournamentWinnersQuery.sparql
│           └── SynergyQuery.sparql
│
└── tests/
    ├── MtgDeckEngine.UnitTests/
    └── MtgDeckEngine.IntegrationTests/
```

---

## Data Sources

### 1. Scryfall — Card Data (prices, oracle text, colours, legality)
- **Base URL:** `https://api.scryfall.com`
- **Auth:** None required. `User-Agent` **and** `Accept` headers are both
  mandatory — `HttpClient` sends no User-Agent by default, so set it explicitly
  on the named client registration.
- **Rate limit:** 500ms (2/sec) for `/cards/search` and `/cards/named`;
  100ms for all other methods.
- **Key endpoints:**
  ```
  GET /cards/named?exact=Xyris, the Writhing Storm
  GET /cards/search?q=commander%3AXYRIS&format=json
  GET /cards/search?q=is%3Agamechanger   ← WotC Game Changers list (53 cards)
  GET /bulk-data   ← index of bulk downloads (see below)
  ```
- **Bulk data format (changed):** the plain-JSON-array `download_uri` /
  `size` fields are **gone**. Entries now expose `jsonl_download_uri` and
  `compressed_size` — a **gzipped JSON Lines** file (~78 MB gz, ~624 MB
  decompressed for `default_cards`). `ScryfallBulkCache` decompresses on the
  way to disk and sniffs array-vs-JSONL when reading, so older caches still load.
- **Fields to extract:** `name`, `oracle_id`, `colors`, `color_identity`,
  `prices.eur`, `prices.usd`, `type_line`, `oracle_text`, `legalities.commander`,
  `game_changer` (bool — WotC's official Game Changer flag, per card)
- **Printing selection matters:** `default_cards` lists **every** printing.
  Many are unpriced (gold-bordered, memorabilia, digital) and a few carry bogus
  near-zero listings. `ScryfallBulkCache.PickPrinting` takes the cheapest
  *purchasable paper* printing above 5% of that card's median price. Getting
  this wrong makes expensive staples look free to the budget builder.
- **Notes:** Use the bulk data download for initial seed; use individual card
  lookups for incremental updates. Oracle ID is the stable card identifier across printings.

### 2. EDHREC — Popularity & Synergy Data
- **Base URL:** `https://json.edhrec.com`
- **Auth:** None required
- **Key endpoints:**
  ```
  GET /pages/commanders/xyris-the-writhing-storm.json
  GET /pages/commanders/{slug}.json    ← any commander slug
  ```
- **Slug format:** lowercase, spaces and commas replaced with hyphens
  e.g. "Atraxa, Praetors' Voice" → `atraxa-praetors-voice`
- **Fields to extract per card:** `name`, `inclusion` (%), `synergy` (score),
  `num_decks`, `potential_decks`, category (creature/removal/ramp/draw/wheel/land etc.)
- **Notes:** The response contains `cardlists` array — each entry is a category
  (e.g. "Ramp", "Card Draw") containing an array of card objects.

### 3. EDHTop16 — Competitive Tournament Performance
- **Base URL:** `https://www.edhtop16.com`
- **Auth:** API available (check https://edhtop16.com/about for current docs)
- **Key data:** tournament results, commander standings, meta share %, pilot profiles
- **Fields to extract:** commander name, placement, wins, tournament name, date,
  deck composition (linked or inline)
- **Notes:** Start by exploring their API docs page. They explicitly offer API
  access for developers.

### 4. Spicerack.gg — Tournament Decklists with Win/Loss Records
- **Base URL:** `https://api.spicerack.gg`
- **Auth:** API key required (free registration at spicerack.gg)
- **Key endpoint:**
  ```
  GET /api/export-decklists/?num_days=90&event_format=COMMANDER&decklist_as_text=true
  ```
- **Response fields per entry:** `TID`, `tournamentName`, `format`, `players`,
  `startDate`, `swissRounds`, `topCut`, and per-player: `name`, `decklist` (Moxfield URL),
  `winsSwiss`, `lossesSwiss`, `draws`, `winsBracket`, `lossesBracket`
- **Notes:** This is the primary source for win/loss ratios. `winsSwiss + winsBracket`
  gives total wins per player per tournament.

### 5. Moxfield — Community Decklists (Phase 2)
- **Base URL:** `https://api.moxfield.com`
- **Auth:** Custom User-Agent required — email support@moxfield.com
- **Key endpoint:** `GET /v2/decks/all/{deckId}` (public decks only)
- **Fields:** commander, mainboard cards, views, likes, last updated
- **Notes:** Do NOT scrape without the custom User-Agent. Add as Phase 2 after
  getting API access. In Phase 1, Spicerack decklists (linked to Moxfield) can
  be fetched individually once you have the User-Agent.

---

## RDF Ontology

### Prefixes

```turtle
@prefix mtg:  <http://example.org/mtg#> .
@prefix rdf:  <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
@prefix owl:  <http://www.w3.org/2002/07/owl#> .
@prefix xsd:  <http://www.w3.org/2001/XMLSchema#> .
@prefix sh:   <http://www.w3.org/ns/shacl#> .
```

### Classes (TBox — the ontology schema)

```turtle
mtg:Card           a owl:Class .    # Any MTG card (oracle identity)
mtg:Commander      a owl:Class ;    # Card that is a legal Commander
                   rdfs:subClassOf mtg:Card .
mtg:Deck           a owl:Class .    # 100-card Commander deck
mtg:Tournament     a owl:Class .    # A competitive event
mtg:TournamentEntry a owl:Class .   # One player's result in one tournament
mtg:Color          a owl:Class .    # W U B R G C
mtg:Category       a owl:Class .    # ramp, draw, removal, wheel, token, land…
```

### Key Properties

```turtle
# Card properties
mtg:hasName          a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:string .
mtg:hasOracleId      a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:string .
mtg:hasColor         a owl:ObjectProperty  ; rdfs:domain mtg:Card ; rdfs:range mtg:Color .
mtg:hasTypeLine      a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:string .
mtg:hasPriceEur      a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:decimal .
mtg:hasPriceUsd      a owl:DatatypeProperty ; rdfs:domain mtg:Card ; rdfs:range xsd:decimal .
mtg:inCategory       a owl:ObjectProperty  ; rdfs:domain mtg:Card ; rdfs:range mtg:Category .

# EDHREC-derived (per commander context — these are commander-scoped)
mtg:hasInclusionPct  a owl:DatatypeProperty ; rdfs:range xsd:decimal . # 0.0–100.0
mtg:hasSynergyScore  a owl:DatatypeProperty ; rdfs:range xsd:decimal . # can be negative
mtg:hasNumDecks      a owl:DatatypeProperty ; rdfs:range xsd:integer .

# Deck properties
mtg:containsCard     a owl:ObjectProperty  ; rdfs:domain mtg:Deck ; rdfs:range mtg:Card .
mtg:hasCommander     a owl:ObjectProperty  ; rdfs:domain mtg:Deck ; rdfs:range mtg:Commander .
mtg:hasSource        a owl:DatatypeProperty ; rdfs:domain mtg:Deck ; rdfs:range xsd:string .
mtg:hasTotalPriceEur a owl:DatatypeProperty ; rdfs:domain mtg:Deck ; rdfs:range xsd:decimal .

# Tournament
mtg:hasName          rdfs:domain mtg:Tournament .
mtg:hasDate          a owl:DatatypeProperty ; rdfs:domain mtg:Tournament ; rdfs:range xsd:date .
mtg:hasPlayerCount   a owl:DatatypeProperty ; rdfs:domain mtg:Tournament ; rdfs:range xsd:integer .
mtg:hasFormat        a owl:DatatypeProperty ; rdfs:domain mtg:Tournament ; rdfs:range xsd:string .

# TournamentEntry
mtg:inTournament     a owl:ObjectProperty  ; rdfs:domain mtg:TournamentEntry ; rdfs:range mtg:Tournament .
mtg:hasDeck          a owl:ObjectProperty  ; rdfs:domain mtg:TournamentEntry ; rdfs:range mtg:Deck .
mtg:hasPlacement     a owl:DatatypeProperty ; rdfs:domain mtg:TournamentEntry ; rdfs:range xsd:integer .
mtg:hasWinsSwiss     a owl:DatatypeProperty ; rdfs:domain mtg:TournamentEntry ; rdfs:range xsd:integer .
mtg:hasLossesSwiss   a owl:DatatypeProperty ; rdfs:domain mtg:TournamentEntry ; rdfs:range xsd:integer .
mtg:hasWinsBracket   a owl:DatatypeProperty ; rdfs:domain mtg:TournamentEntry ; rdfs:range xsd:integer .
```

### URI Strategy

Use stable, source-agnostic URIs:
```
Cards:       mtg:card/{oracle-id}          e.g. mtg:card/47a6234f-309f-...
Commanders:  mtg:commander/{slug}          e.g. mtg:commander/xyris-the-writhing-storm
Decks:       mtg:deck/{source}/{source-id} e.g. mtg:deck/spicerack/T-12345-p3
Tournaments: mtg:tournament/{source}/{id}  e.g. mtg:tournament/edhtop16/E-789
Colors:      mtg:color/W  mtg:color/U  mtg:color/B  mtg:color/R  mtg:color/G
Categories:  mtg:category/ramp  mtg:category/draw  mtg:category/removal ...
```

---

## SHACL Validation Shapes

```turtle
# File: src/MtgDeckEngine.Ontology/mtg-shapes.ttl

mtg:DeckShape a sh:NodeShape ;
  sh:targetClass mtg:Deck ;
  sh:property [
    sh:path mtg:hasCommander ;
    sh:minCount 1 ;
    sh:maxCount 1 ;
    sh:class mtg:Commander ;
    sh:message "A deck must have exactly one Commander."
  ] ;
  sh:property [
    sh:path mtg:containsCard ;
    sh:minCount 99 ;
    sh:maxCount 100 ;
    sh:message "A Commander deck must contain 99 or 100 cards."
  ] .

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
    sh:datatype xsd:string
  ] .

mtg:TournamentEntryShape a sh:NodeShape ;
  sh:targetClass mtg:TournamentEntry ;
  sh:property [ sh:path mtg:inTournament ; sh:minCount 1 ] ;
  sh:property [ sh:path mtg:hasDeck      ; sh:minCount 1 ] ;
  sh:property [ sh:path mtg:hasPlacement ; sh:minCount 1 ; sh:datatype xsd:integer ] .
```

---

## Key SPARQL Queries

### 1. Core Xyris Cards by Win Rate + Budget
```sparql
PREFIX mtg: <http://example.org/mtg#>

SELECT ?cardName ?placement ?inclusion ?priceEur WHERE {
  # Cards appearing in Xyris decks that reached Top 4
  ?entry mtg:hasCommander mtg:commander/xyris-the-writhing-storm ;
         mtg:hasPlacement  ?placement ;
         mtg:hasDeck       ?deck .
  FILTER(?placement <= 4)

  ?deck mtg:containsCard ?card .
  ?card mtg:hasName        ?cardName ;
        mtg:hasPriceEur    ?priceEur .

  OPTIONAL { ?card mtg:hasInclusionPct ?inclusion }

  FILTER(?priceEur < 5.0)
}
ORDER BY DESC(?placement) DESC(?inclusion)
```

### 2. Popularity vs Performance Gap (cards popular but never win)
```sparql
PREFIX mtg: <http://example.org/mtg#>

SELECT ?cardName ?inclusion (COUNT(?entry) AS ?topCutAppearances) WHERE {
  ?card mtg:hasName        ?cardName ;
        mtg:hasInclusionPct ?inclusion .
  FILTER(?inclusion > 40)

  OPTIONAL {
    ?entry mtg:hasPlacement ?p ; mtg:hasDeck ?deck .
    FILTER(?p <= 8)
    ?deck mtg:containsCard ?card .
  }
}
GROUP BY ?cardName ?inclusion
HAVING (COUNT(?entry) = 0)   # Popular but zero top-cut appearances
ORDER BY DESC(?inclusion)
```

### 3. Budget Deck Builder (closest to optimal within budget)
```sparql
PREFIX mtg: <http://example.org/mtg#>

SELECT ?deck ?totalPrice (COUNT(?card) AS ?cardCount) WHERE {
  ?deck mtg:hasCommander mtg:commander/xyris-the-writhing-storm ;
        mtg:hasTotalPriceEur ?totalPrice .
  FILTER(?totalPrice <= 100.0)    # €100 budget

  ?entry mtg:hasDeck ?deck ;
         mtg:hasPlacement ?p .
  FILTER(?p <= 16)               # must have top-cut

  ?deck mtg:containsCard ?card .
}
GROUP BY ?deck ?totalPrice
ORDER BY DESC(?p) ASC(?totalPrice)
LIMIT 5
```

### 4. Card Co-occurrence (synergy packages)
```sparql
PREFIX mtg: <http://example.org/mtg#>

SELECT ?nameA ?nameB (COUNT(?deck) AS ?coAppearances) WHERE {
  ?deck mtg:hasCommander mtg:commander/xyris-the-writhing-storm ;
        mtg:containsCard ?cardA ;
        mtg:containsCard ?cardB .
  ?cardA mtg:hasName ?nameA ; mtg:inCategory mtg:category/wheel .
  ?cardB mtg:hasName ?nameB ; mtg:inCategory mtg:category/wheel .
  FILTER(?cardA != ?cardB && STR(?nameA) < STR(?nameB))
}
GROUP BY ?nameA ?nameB
HAVING (COUNT(?deck) > 5)
ORDER BY DESC(?coAppearances)
```

### 5. Budget Swap Suggestions
```sparql
PREFIX mtg: <http://example.org/mtg#>

# "What can replace Wheel of Fortune (€40) under €5 with similar synergy?"
SELECT ?altName ?altPrice ?altInclusion WHERE {
  # Find the expensive card's category
  mtg:card/wheel-of-fortune mtg:inCategory ?cat .

  # Find alternatives in same category
  ?altCard mtg:inCategory       ?cat ;
           mtg:hasName          ?altName ;
           mtg:hasPriceEur      ?altPrice ;
           mtg:hasInclusionPct  ?altInclusion .

  FILTER(?altPrice < 5.0)
  FILTER(?altCard != mtg:card/wheel-of-fortune)
}
ORDER BY DESC(?altInclusion)
LIMIT 5
```

---

## API Endpoints to Build

```
GET  /api/commanders/{slug}/recommendations
     ?maxPriceEur=50&minInclusion=20&source=tournament|edhrec|all
     → Ranked card list for this commander within budget

GET  /api/commanders/{slug}/meta
     → Tournament win rates, top placements, meta share %

GET  /api/commanders/{slug}/cards/top
     ?category=wheel|ramp|draw|removal&limit=20
     → Top cards by category for this commander

GET  /api/commanders/{slug}/budget-builds
     ?maxTotalEur=100
     → Closest tournament-proven decklists within budget

GET  /api/cards/{oracleId}/swap-suggestions
     ?maxPriceEur=5
     → Budget alternatives in same category

GET  /api/commanders/{slug}/build-deck
     ?totalBudgetEur=150&maxBracket=3&maxCardPriceEur=20
     → Complete 99-card deck within budget, capped at the given Commander
       Bracket. Ranked by real tournament win rate (see below).

GET  /api/commanders/discover
     ?maxBracket=3&maxBudgetEur=200&minDeckCount=3
     → The inverse of the deck builder: which commanders suit this bracket and
       budget? Grouped by command zone (partner pairs count once), ranked by the
       Wilson lower bound of tournament win rate so thin samples sink.

GET    /api/decks                → saved decks, newest first (?commander=slug)
GET    /api/decks/{id}           → one saved deck with its full card list
GET    /api/decks/{id}/export    → text/plain "N Card Name" list (Moxfield/Archidekt)
DELETE /api/decks/{id}           → delete a saved deck
POST   /api/decks/build-and-save → build + persist in one call (what the UI uses)

GET  /api/commanders/{slug}/build-deck/export
     ?totalBudgetEur=120&maxBracket=3
     → Same build, returned as a pasteable decklist instead of JSON

GET  /api/ingest/trigger
     → Manually trigger ingestion cycle (dev only)

GET  /health      → ASP.NET Core health check endpoint
```

---

## ETL Worker Pattern

Each ingestor implements the same interface:

```csharp
// src/MtgDeckEngine.Core/Interfaces/IIngestorService.cs
public interface IIngestorService
{
    string SourceName { get; }
    Task IngestAsync(CancellationToken ct);
}
```

Each worker:
1. Fetches JSON from the source API
2. Maps to intermediate DTOs
3. Builds RDF triples using dotNetRDF `IGraph`
4. Runs SHACL validation (`ShaclProcessor.Validate(graph, shapesGraph)`)
5. Writes validated triples to Fuseki via `SparqlUpdateClient`

Rate limiting: use `Polly` with exponential backoff + jitter for all HTTP clients.
Retry policy: 3 retries, 429 → respect `Retry-After` header.

### dotNetRDF snippet — building triples

```csharp
var g = new Graph();
g.NamespaceMap.AddNamespace("mtg", new Uri("http://example.org/mtg#"));

var card = g.CreateUriNode(new Uri($"http://example.org/mtg#card/{oracleId}"));
var hasName = g.CreateUriNode("mtg:hasName");
var rdfType  = g.CreateUriNode(RdfSpecsHelper.RdfType);
var cardClass = g.CreateUriNode("mtg:Card");

g.Assert(card, rdfType,  cardClass);
g.Assert(card, hasName,  g.CreateLiteralNode(name));
g.Assert(card, g.CreateUriNode("mtg:hasPriceEur"),
               g.CreateLiteralNode(price.ToString(), new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal)));
```

### Seeing the SPARQL a request actually runs

The deck queries are generated from request parameters, so the files in
`queries/` are examples, not the real thing. Add `explain=true` to any of
`/recommendations`, `/build-deck`, `/meta` or `/discover` and you get the exact
query for those parameters as `text/plain`, with the rationale as SPARQL
comments so the output is runnable as-is:

```bash
bin/explain '/api/commanders/xyris-the-writhing-storm/build-deck?totalBudgetEur=120&maxBracket=3'
bin/explain '/api/commanders/discover?maxBracket=3&maxBudgetEur=200' --run
bin/explain '/api/commanders/xyris-the-writhing-storm/meta' --save out/
```

Two things the SPARQL will not show you, because they do not happen in the
graph: **ranking and packing are C#**, over the rows the query returns (Wilson
lower bound for commanders, the shrunk win-rate blend for cards, the greedy
budget knapsack), and the **Commander Bracket comes from Commander Spellbook
over HTTP**. Re-running the query and sorting by `winRate` will not reproduce
the API's ordering.

### Commander slugs must match EDHREC exactly

`MtgVocab.Slugify` keys three separate things: EDHREC's page URL, the Scryfall
bulk cache's slug index, and every `mtg:commander/{slug}` URI in the graph. If
it disagrees with EDHREC by one character, the commander's data lands under one
slug and its identity node under another, and they never join.

The rules that a naive character map gets wrong: double-faced cards slug on the
**front face only** (`Kefka, Court Mage // Kefka, Ruler of Ruin` →
`kefka-court-mage`), apostrophes are **dropped** rather than turned into
separators (`Clachan's Heart` → `clachans-heart`), and diacritics are folded.
The original version passed unknown characters straight through, which put
`//`, `&` and `û` into 47 commander URIs.

**The EDHREC ingestion path must assert `mtg:commander/{slug}` itself.**
`ScryfallToRdfMapper.AssertCard` types the *card* node as a Commander, which is
a different subject. Every commander-facing query keys on the slug node, so a
commander ingested by name was invisible in the commander list until that node
existed.

### `maxBracket` is enforced in two passes

Card-level triggers — Game Changers, mass land denial, extra turns — are
readable from card flags, so the builder constrains those while building.

**Two-card infinite combos are not.** They are a property of card *pairs*, only
Commander Spellbook knows them, and they are a hard Bracket-4/5 trigger. A build
could therefore honour the Game Changer cap perfectly and still come back cEDH,
which is exactly what happened to Kefka (whose commander combos with Psychosis
Crawler).

So the cap is enforced afterwards instead: evaluate, and if the bracket
overshoots, cut cards until every reported combo is broken, refill to 99 and
re-evaluate. Up to 3 rounds, converging in 1 in practice.

Which cards to cut is a **minimum hitting set** over the combos
(`ComboBreaker`), not one cut per combo. Combos share pieces — Kefka's three
run `Dualcaster Mage + Blur` and `Dualcaster Mage + Ghostly Flicker`, so one
cut covers both. Cutting per combo removes 3 cards there; the minimum cover
removes 2. Every avoided cut is a card the budget paid for and the scorer
wanted. Ties between equally small covers go to the one losing least score.

The search is exhaustive by size, which is exact and trivially fast at this
scale (a handful of participants). Past 22 distinct participants it falls back
to greedy set cover so a pathological list cannot hang a request.

Two details that make it terminate:
- Cut cards go on a ban list. Without it the upgrade pass immediately re-picks
  the combo piece, since that is the card it rates highest.
- A combo running through the commander cannot be broken — the commander is not
  one of the 99. The loop detects that it removed nothing and reports the real
  bracket rather than spinning.

Caps of 4 and 5 permit combos, so no repair runs and no extra call is made.

### Tournament decks carry their commander (and their rollups)

TopDeck.gg wraps EDH lists in `~~Commanders~~` / `~~Mainboard~~` sections. The
parser used to skip the header and treat the commander as an ordinary card, so
27k tournament decks had no `mtg:hasCommander` — only the single EDHREC-ingested
commander did. Fixing that took the graph from 1 commander with tournament decks
to **733**, which is also what makes the budget builder usable beyond Xyris.

Partner and background pairs put two cards in the command zone, so `hasCommander`
is asserted per card and consumers must group by the *pair*, not the card — a
third of EDH decks are pairs, and crediting the pair's record to each half
separately ranks partner halves above real single commanders.

Ingestion also precomputes `hasTotalPriceEur`, `hasGameChangerCount` and
`hasPricedCardCount` per deck. Deriving those at query time would mean summing
~100 card prices per deck across tens of thousands of decks on every request.

### Canonical xsd:decimal is mandatory (cost us a silent data-corruption bug)

Always build decimal literals with `RdfLiterals.Decimal(value)`, never
`value.ToString()`.

Jena accepts a non-canonical `xsd:decimal` on INSERT and hands back the
canonical form on SELECT — but the triple then becomes **permanently
undeletable**. `DELETE ... WHERE`, `DELETE WHERE`, and even `DELETE DATA` with
the exact original lexical form all report success and remove nothing.

Canonical form needs a decimal point with at least one digit after it and no
trailing zeros beyond that. C# produces non-canonical output routinely:

| C# value | `ToString()` | Canonical | Deletable |
|---|---|---|---|
| `60m` (JSON round-trip) | `"60"` | `"60.0"` | no |
| `1.50m` (price feed) | `"1.50"` | `"1.5"` | no |
| `0m` | `"0"` | `"0.0"` | no |
| `0.45m` | `"0.45"` | `"0.45"` | yes |

Because delete-before-insert silently no-ops on these, every re-ingest added a
second price triple: 8,601 of 15,561 priced cards had accumulated duplicates,
and `MIN(?price)` was quoting whichever was lowest. There is no repair query —
affected data has to be dropped and re-ingested.

### Fuseki request-size limit (bit us once)

Fuseki posts SPARQL Update as an HTTP form, and Jetty rejects bodies over
**20 MB** with `form too large` — which reaches the client as a bare HTTP 500
with no hint of the cause. `FusekiGraphRepository.WriteAsync` therefore splits
every write into ≤8 MB requests by serialised size. Callers must not try to
batch around this themselves: `TopDeckIngestor` used to flush every 25
tournaments, but a single large EDH event is tens of thousands of triples, so
the batch was measured in the wrong unit and silently failed the entire
TopDeck ingest.

### dotNetRDF snippet — querying via SparqlQueryClient

```csharp
var client = new SparqlQueryClient(
    new HttpClient(),
    new Uri("http://localhost:3030/mtg/sparql"));

var results = await client.QueryWithResultSetAsync(sparqlQuery, ct);
foreach (SparqlResult row in results)
{
    var cardName = (row["cardName"] as ILiteralNode)?.Value;
    var price    = decimal.Parse((row["priceEur"] as ILiteralNode)?.Value ?? "0");
}
```

### CDC Alternative — Outbox + Debezium (Phase 2+)

The polling `BackgroundService` relay is fine for MVP. When volume grows or event
replay is needed, replace it with CDC (Change Data Capture) via Debezium:

**How it works:** Debezium reads PostgreSQL's WAL (Write-Ahead Log) — every INSERT
into `outbox_messages` becomes a Kafka event within ~100ms. No polling, no
`Published` column, and Kafka retains the log for replay.

**PostgreSQL WAL setup:**
```sql
ALTER SYSTEM SET wal_level = 'logical';  -- requires restart
-- Outbox table — no Published column needed
CREATE TABLE outbox_messages (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type   TEXT NOT NULL,   -- used for Kafka topic routing
    aggregate_id TEXT NOT NULL,
    payload      JSONB NOT NULL,
    created_at   TIMESTAMPTZ DEFAULT NOW()
);
```

**Debezium connector config** (POST to `http://localhost:8083/connectors`):
```json
{
  "name": "mtg-outbox-connector",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "postgres",
    "database.port": "5432",
    "database.user": "mtguser",
    "database.password": "secret",
    "database.dbname": "mtgengine",
    "plugin.name": "pgoutput",
    "table.include.list": "public.outbox_messages",
    "transforms": "outbox",
    "transforms.outbox.type": "io.debezium.transforms.outbox.EventRouter",
    "transforms.outbox.table.field.event.id":      "id",
    "transforms.outbox.table.field.event.type":    "event_type",
    "transforms.outbox.table.field.event.payload": "payload",
    "transforms.outbox.route.by.field":            "event_type",
    "transforms.outbox.route.topic.replacement":   "mtg.${routedByValue}"
  }
}
```

The `EventRouter` SMT routes each outbox row to a Kafka topic named after
`event_type` — a `CardIngested` row goes to `mtg.CardIngested` automatically.

**C# Kafka consumer** (`dotnet add package Confluent.Kafka`):
```csharp
public class CardIngestedConsumer : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092",
            GroupId          = "mtg-graph-writer",
            AutoOffsetReset  = AutoOffsetReset.Earliest,
            EnableAutoCommit = false  // manual commit for idempotency control
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe("mtg.CardIngested");
        while (!ct.IsCancellationRequested)
        {
            var result  = consumer.Consume(ct);
            var eventId = result.Message.Key; // Debezium uses outbox.id as Kafka key
            // Idempotency: Redis SET NX (set-if-not-exists), TTL 24h
            var isNew = await _redis.StringSetAsync(
                $"processed:{eventId}", "1",
                TimeSpan.FromHours(24), When.NotExists);
            if (isNew)
            {
                var evt = JsonSerializer.Deserialize<CardIngestedEvent>(result.Message.Value);
                await _graph.UpsertCardTriplesAsync(evt!, ct);
            }
            consumer.Commit(result); // manual commit after processing
        }
    }
}
```

**Polling relay vs CDC comparison:**

| | Polling relay (Phase 1) | CDC / Debezium (Phase 2+) |
|---|---|---|
| Latency | 1–10s | <100ms |
| DB load | periodic SELECT | WAL read (zero queries) |
| `Published` column | required | not needed |
| Event replay | no | yes (Kafka log retention) |
| Complexity | low | medium (needs Kafka) |

---

## Docker Setup

```yaml
# docker-compose.yml
services:
  fuseki:
    image: stain/jena-fuseki:latest
    ports:
      - "3030:3030"
    environment:
      - ADMIN_PASSWORD=admin
      - FUSEKI_DATASET_1=mtg
    volumes:
      - fuseki-data:/fuseki
      - ./docker/fuseki/config.ttl:/fuseki/configuration/mtg.ttl

  api:
    build: ./src/MtgDeckEngine.Api
    ports:
      - "5000:8080"
    environment:
      - Fuseki__Endpoint=http://fuseki:3030/mtg
      - Fuseki__UpdateEndpoint=http://fuseki:3030/mtg/update
    depends_on:
      - fuseki

volumes:
  fuseki-data:
```

**Optional: CDC stack (Phase 2+)** — add to docker-compose.yml when ready for Debezium:
```yaml
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: mtgengine
      POSTGRES_USER: mtguser
      POSTGRES_PASSWORD: secret
    command: ["postgres", "-c", "wal_level=logical"]
    volumes: [postgres-data:/var/lib/postgresql/data]

  zookeeper:
    image: confluentinc/cp-zookeeper:7.5.0
    environment: { ZOOKEEPER_CLIENT_PORT: 2181 }

  kafka:
    image: confluentinc/cp-kafka:7.5.0
    ports: ["9092:9092"]
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
    depends_on: [zookeeper]

  debezium:
    image: debezium/connect:2.7
    ports: ["8083:8083"]
    environment:
      BOOTSTRAP_SERVERS: kafka:9092
      GROUP_ID: mtg-cdc
      CONFIG_STORAGE_TOPIC: debezium_config
      OFFSET_STORAGE_TOPIC: debezium_offsets
      STATUS_STORAGE_TOPIC: debezium_status
    depends_on: [kafka, postgres]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]  # for recommendation cache + idempotency keys

# add to volumes section:
# postgres-data:
```

**Useful commands:**
```bash
# Start Fuseki locally
docker compose up fuseki

# Fuseki admin UI
open http://localhost:3030

# Run API
cd src/MtgDeckEngine.Api && dotnet run

# Run tests
dotnet test

# Query Fuseki directly (curl)
curl -X POST http://localhost:3030/mtg/sparql \
  -H "Content-Type: application/sparql-query" \
  -H "Accept: application/sparql-results+json" \
  --data "SELECT * WHERE { ?s ?p ?o } LIMIT 10"

# Load a .ttl file into Fuseki
curl -X POST http://localhost:3030/mtg/data \
  -H "Content-Type: text/turtle" \
  --data-binary @src/MtgDeckEngine.Ontology/mtg-ontology.ttl
```

---

## Implementation Phases

### Phase 1 — MVP (target: working Xyris recommendations)
1. Scaffold solution structure (`dotnet new sln`, add projects)
2. `docker-compose.yml` + Fuseki running locally
3. `mtg-ontology.ttl` — define all classes and properties
4. `mtg-shapes.ttl` — SHACL shapes for Deck, Card, TournamentEntry
5. `ScryfallClient` + `ScryfallToRdfMapper` — ingest card data
6. `EdhrecClient` + `EdhrecToRdfMapper` — ingest Xyris inclusion/synergy
7. `FusekiGraphRepository` — write/query via dotNetRDF
8. `GET /api/commanders/xyris-the-writhing-storm/recommendations?maxPriceEur=10`

**Definition of done:** Can query Xyris cards by budget, ranked by EDHREC inclusion %.

### Phase 2 — Tournament Signals
1. `EdhTop16Client` + mapper — tournament data, placements
2. `SpicerackClient` + `TournamentToRdfMapper` — wins/losses per entry
3. Tournament-aware SPARQL queries (popularity vs performance gap)
4. `GET /api/commanders/{slug}/meta` endpoint
5. Extend recommendations to weight tournament appearances

**Definition of done:** Can answer "cards in Top 4 Xyris decks under €5".

### Phase 3 — Multi-commander + Moxfield + AI Layer
1. Generalise ingestors to any commander slug (not just Xyris)
2. Moxfield integration (after requesting User-Agent)
3. Budget swap engine (`/api/cards/{id}/swap-suggestions`)
4. Budget deck builder (`/api/commanders/{slug}/budget-builds`)
5. AI layer: LLM endpoint that receives current deck list, queries the graph
   via `IGraphRepository`, and generates natural language recommendations
   → ties directly to the "leveraging AI capabilities" requirement

### Phase 4 — AWS / Production Path (LEGO portfolio alignment)
1. Replace Fuseki with **Amazon Neptune** (RDF/SPARQL mode)
   - Neptune speaks the same SPARQL 1.1 protocol — swap only the endpoint URL
   - Use IRSA for the API pod to access Neptune (no hardcoded AWS credentials)
2. Deploy API to **EKS** with Deployment + Service + Ingress (ALB)
3. Background workers as **Kubernetes CronJobs** (nightly ingestion)
4. Observability: CloudWatch + X-Ray traces on HTTP calls and SPARQL queries
5. CI/CD: GitHub Actions → ECR → EKS (GitOps with Argo CD optional)

---

## Architectural Patterns

### Polyglot Persistence Strategy

This project intentionally uses multiple persistence technologies, each for what
it does best:

| Store | Technology | Purpose |
|---|---|---|
| Knowledge graph | Neptune / Fuseki | RDF triples, SPARQL traversals, card/deck relationships |
| Recommendation cache | Redis / ElastiCache | <10ms response for repeated queries; TTL-based invalidation |
| Tournament data | PostgreSQL | ACID writes, relational FK constraints (entry → deck → tournament) |
| Full-text card search | OpenSearch (Phase 3) | Oracle text search, faceted filtering by color/type |

**Decision framework (interview answer):**
Start with: "Need ACID + joins?" → SQL. "Highly connected traversals?" → Graph.
"Full-text?" → Search. "High write/time-series?" → Column. "O(1) cache?" → KV.
"Default?" → Document. The key principle: **no single database is best for every
access pattern** — polyglot persistence assigns each responsibility to the right tool.

**CQRS connection:** Write commands go to the triplestore (the source of truth);
read models are projected from graph events into Redis (hot recommendations) and
PostgreSQL (structured reporting). Same data, different shapes for different
access patterns.

**CAP Theorem awareness:** Neptune/Fuseki are CP (consistency + partition tolerance).
Redis is AP (availability + eventual consistency for cache). PostgreSQL is CP.
When Neptune is unavailable, serve stale recommendations from Redis rather than
failing — cache as availability layer.

---

### Outbox Pattern

**Problem:** Writing to the triplestore AND publishing a "CardIngested" event to
EventBridge are two separate systems. If the event publish fails after the graph
write, the downstream consumers are never notified.

**Solution:** Write both the business data AND the outbox row in the **same DB
transaction**. A relay (polling or CDC) publishes the outbox rows asynchronously.

```
Application → BEGIN TX
               INSERT INTO cards (...)          -- business data
               INSERT INTO outbox_messages (...) -- event record
              COMMIT TX
Relay/CDC   → reads outbox → publishes to Kafka/EventBridge → marks published
```

**Why not 2PC (Two-Phase Commit)?**
SQS, Kafka, SNS, and EventBridge do not implement the XA protocol. They cannot
be enlisted in a `TransactionScope`. Even if you wrap both calls in a
`TransactionScope`, the broker call fires immediately and cannot be rolled back.
The Outbox avoids cross-system coordination by using a single DB transaction —
the only reliable atomic primitive available.

**Phase 1:** Polling `BackgroundService` relay (simple, no extra infra).
**Phase 2+:** Debezium CDC reads PostgreSQL WAL — lower latency, no polling,
Kafka log retention enables event replay. See CDC section above.

---

### Idempotency in Consumers

SQS and Kafka both guarantee **at-least-once delivery** — the same event can
arrive twice (retry after timeout, rebalance, etc.). Consumers MUST be idempotent.

**Strategy for this project:**
1. RDF `INSERT DATA` is naturally idempotent — inserting the same triple twice
   is a no-op in any triplestore. Graph writes are safe by default.
2. For side effects (calling external APIs, sending notifications): use
   Redis `SET NX` with TTL as a deduplication store, keyed on the event's ID.
3. SQS FIFO queues: use `MessageDeduplicationId` for built-in 5-minute dedup.

```csharp
// Pattern: Redis SET NX (set-if-not-exists)
var isNew = await _redis.StringSetAsync(
    key:    $"processed:{eventId}",
    value:  "1",
    expiry: TimeSpan.FromHours(24),
    when:   When.NotExists);
if (!isNew) return; // already processed — discard
```

---

### Event-Driven Patterns (Three Types)

**Event Notification** — minimal payload, just announces something happened.
Consumer must call back to get state. Low coupling but extra round-trip.
```json
{ "type": "CardIngested", "cardId": "abc-123" }
```

**Event-Carried State Transfer** — full state in payload. Consumer has everything
it needs without a callback. Higher payload size but zero extra calls. Preferred
for this project — graph writes need the full card data.
```json
{ "type": "CardIngested", "cardId": "abc-123", "name": "Xyris", "priceEur": 3.50 }
```

**Event Sourcing** — events ARE the source of truth; current state is rebuilt by
replaying the event log. Not used here — adds complexity only justified when
audit history or time-travel queries are core business requirements.

---

### AWS Event Services (Phase 4)

| Service | Pattern | Use case in this project |
|---|---|---|
| SQS | Point-to-point queue, at-least-once | Worker queues for ingestors (Scryfall, EDHREC) |
| SNS + SQS | Fan-out (1 event → N queues) | "CardPriceUpdated" → graph writer + cache invalidator |
| EventBridge | Content-based routing by payload | Route events by `source` field (scryfall vs edhrec vs spicerack) |
| Kinesis | High-throughput streaming, replay | Not needed at this scale; applicable if ingesting full Scryfall bulk updates in real time |

**Idiomatic AWS fan-out:** SNS topic → multiple SQS queues (one per consumer),
each SQS queue with its own DLQ. This gives each consumer independent retry
behaviour, dead-letter inspection, and processing speed.

---

### Schema Evolution

As the ontology evolves, existing triples in the graph must remain valid. Rules:
- **Additive changes only:** adding new optional properties is safe.
- **Never rename URIs** — existing triples reference the old URI. Use `owl:deprecated`
  to mark old properties and add `owl:equivalentProperty` to the new one.
- **SHACL shapes are versioned** — keep old shapes active during migration windows.
- For Kafka events: use Avro + AWS Glue Schema Registry in Phase 4 to enforce
  backward/forward compatibility on event contracts.

---

## Coding Conventions

- Use `IGraphRepository` abstraction everywhere — `FusekiGraphRepository` for
  prod, `InMemoryGraphRepository` (dotNetRDF in-memory graph) for unit tests.
- Store `.sparql` files as embedded resources in `MtgDeckEngine.Graph/Queries/`.
  Load at startup with `Assembly.GetManifestResourceStream`.
- All HTTP clients registered via `IHttpClientFactory` in DI.
- Polly retry + circuit breaker on all external HTTP calls.
- Ingestors are idempotent: upserting a triple that already exists is a no-op
  in Fuseki (use SPARQL UPDATE with `INSERT DATA` inside `IF NOT EXISTS` or
  delete-before-insert for mutable properties like price).
- SHACL validation runs before every `IGraphRepository.WriteAsync` call.
  Invalid graphs throw `ShaclValidationException` with the violation report.
- Keep EDHREC inclusion % and synergy scores in commander-scoped named graphs:
  `GRAPH <http://example.org/mtg/context/xyris>` so they don't pollute global
  card data (a card can have different synergy with different commanders).

---

## Key Decisions & Rationale

| Decision | Rationale |
|---|---|
| RDF over LPG (e.g. Neo4j) | LEGO's stack uses RDF/OWL/SHACL/SPARQL; this project directly demonstrates those skills |
| dotNetRDF over raw HTTP | Native .NET, SHACL built-in, Leviathan for in-memory tests, same SPARQL API for local Fuseki and Neptune |
| Fuseki locally, Neptune in prod | Zero-cost local dev; Neptune is AWS-managed and aligns with LEGO's AWS-first infrastructure |
| Named graphs per source | EDHREC inclusion % for Xyris ≠ global card property; named graphs scope the signal correctly |
| Spicerack over scraping EDHTop16 | Spicerack has a documented public API with win/loss data; less maintenance risk |

---

## Context: Why This Project Matters

This project was designed as a portfolio piece aligned with a Senior Software Engineer
role at **The LEGO Group** (Digital Technology / Product Delivery). The job requires:

- C#/.NET with strong architecture ✓
- Cloud-native AWS ✓ (Phase 4)
- Ontology engineering, semantic modeling, knowledge graphs ✓ (core of the project)
- Data integration patterns ✓ (multi-source ETL)
- Event-driven / distributed systems ✓ (background workers)
- CI/CD and DevOps ✓ (Phase 4)
- Kubernetes ✓ (Phase 4)
- AI capabilities integration ✓ (Phase 3)

The MTG domain is personal (Commander player) and makes the project genuine, not just
a contrived exercise. Use it as a talking point: "I built a knowledge graph engine
for my Commander hobby that uses the same RDF/OWL/SHACL/SPARQL stack that LEGO
uses in their Product Delivery organisation."

---

## Interview Quick Reference

Concepts studied and practised for the LEGO Senior Software Engineer role.

### Knowledge Graphs & Ontology Engineering

| Concept | One-liner |
|---|---|
| Triple | subject → predicate → object; the atomic unit of RDF |
| TBox | Terminology Box — ontology schema: classes, properties, rules (OWL) |
| ABox | Assertion Box — instances: the actual data (the knowledge graph) |
| OWL | Defines classes + inference; open-world (absence ≠ false) |
| SHACL | Validates shapes against a contract; closed-world (absence = violation) |
| SPARQL | SQL for RDF graphs; pattern-matches triples with variables |
| RDF vs LPG | RDF = W3C standard, triples, SPARQL, semantic inference; LPG = Neo4j, Cypher, properties on edges |
| Named graphs | Scope triples to a context (e.g. EDHREC inclusion % per commander, not global) |
| Competency questions | "What must the ontology answer?" — written before modeling, used as acceptance criteria |

**OWL vs SHACL in one sentence:** OWL infers new facts from what's declared;
SHACL rejects data that violates the declared contract.

---

### Kubernetes & CI/CD

| Concept | One-liner |
|---|---|
| Deployment | Manages ReplicaSets; rolling updates, rollback, stateless apps |
| StatefulSet | Pods with stable identity + persistent storage; databases, brokers |
| Service | Stable IP/DNS in front of ephemeral Pods; ClusterIP/NodePort/LoadBalancer |
| Ingress | L7 HTTP routing; on AWS uses ALB via Load Balancer Controller |
| HPA | Scales replicas by CPU/memory or custom metrics |
| Liveness probe | "Is the process alive?" — failure triggers container restart |
| Readiness probe | "Ready for traffic?" — failure removes from load balancer, does NOT restart |
| Startup probe | "Still booting?" — holds liveness checks during slow startup |
| IRSA | IAM Roles for Service Accounts — correct way to give AWS permissions to a Pod (no hardcoded keys) |
| GitOps | Desired state in Git; Argo CD/Flux pulls and reconciles — rollback = git revert |

**Probe rule:** external dependencies (DB, external API) go in **readiness**, never
in **liveness**. Putting a DB check in liveness causes cascade restarts when DB
oscillates.

**CI/CD pipeline:** `git push` → build + test → image scan → push to ECR (tag = SHA)
→ Argo CD detects new tag → applies to EKS → rolling update.

---

### Data Integration · Persistence · Event-Driven

| Concept | One-liner |
|---|---|
| ETL | Transform before load; shape data to destination schema |
| ELT | Load raw first, transform in destination (data warehouse pattern) |
| CDC | Read DB transaction log (WAL); every change becomes an event; Debezium is the connector |
| Outbox Pattern | Write business data + outbox row in same TX; relay publishes async — no 2PC needed |
| 2PC not viable | SQS/Kafka/SNS don't implement XA; TransactionScope can't enlist them |
| CQRS | Separate write model (source of truth) from read models (projections for fast queries) |
| Polyglot persistence | Different DB types for different access patterns; Neptune + Redis + PostgreSQL + OpenSearch |
| CAP Theorem | Can only guarantee 2 of 3: Consistency, Availability, Partition tolerance |
| Eventual consistency | AP systems (DynamoDB, Redis) converge; design for it with idempotency + conflict resolution |
| Idempotency | Redis SET NX with TTL; or SQS FIFO MessageDeduplicationId; RDF INSERT is naturally idempotent |
| Event notification | Minimal payload — consumer calls back for state |
| Event-carried state | Full state in payload — no callback needed; preferred for this project |
| Event sourcing | Events ARE the source of truth; replay to rebuild state; justified only when audit history is core business need |
| SNS fan-out | 1 publish → N SQS queues (one per consumer) + DLQ per queue |
| EventBridge | Content-based routing by JSON payload pattern; better than SNS when routing logic is needed |
| Schema evolution | Additive changes only; Avro + Schema Registry for Kafka contracts |

**Outbox in one sentence:** Both writes go to the same database in the same
transaction — no cross-system coordination, no 2PC, no lost events.

**Persistence decision shortcut:**
ACID + joins → SQL · Connected traversals → Graph · Full-text → Search ·
Write-heavy/IoT → Column · O(1) cache → Key-Value · Default → Document.
