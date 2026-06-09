# SHACL and the budget deck builder — what each does (and doesn't)

> Pair with [`docs/STUDY.md`](STUDY.md) §3 and §4 (SHACL · SPARQL)
> and the source files referenced inline.

## The misconception worth clearing up first

> *"SHACL chooses the best deck for the price."*

It doesn't. SHACL is a **closed-world validation** language — its only job
is to answer **"does this graph conform to a contract?"** with yes/no plus a
list of violations. It never:

- ranks anything
- compares two options
- picks cards
- does cost optimisation

The budget deck builder in this project is a **plain C# greedy algorithm**
that runs *after* a SPARQL query returns candidate cards. SHACL never
enters the loop.

```
                ┌─────────────────────────┐
   SPARQL ────► │ DeckRecommendationSvc   │ ──► ranked card list
                │   (Graph project)       │
                └─────────────────────────┘
                            │
                            ▼
                ┌─────────────────────────┐
                │ BuildBudgetDeckAsync    │ ──► 99-card deck
                │   (quota-aware greedy)  │
                └─────────────────────────┘
                            ▲
                            │ never calls
                ┌─────────────────────────┐
                │ ShaclValidator          │
                │   (Validation project)  │
                └─────────────────────────┘
```

## So what does SHACL actually do here?

It guards the **ingestion boundary**. If we wire it in (today it's coded but
not called — see `STUDY.md` §3 *"When does it run?"*), the flow becomes:

```
Scryfall / EDHREC / EDHTop16 / TopDeck JSON
         │
         ▼
   Mapper produces RDF triples
         │
         ▼
   ShaclValidator.Validate(graph)  ◄── HERE
         │
   pass? ▼ fail? → throw ShaclValidationException, refuse to write
         │
   IGraphRepository.WriteAsync(graph)
```

The shapes in `src/MtgDeckEngine.Ontology/mtg-shapes.ttl` enforce things like:

| Shape | Says |
|---|---|
| `mtg:CardShape` | A `mtg:Card` MUST have one `mtg:hasOracleId` (string) and one `mtg:hasName`. |
| `mtg:DeckShape` (current, relaxed) | A `mtg:Deck` MUST declare its `mtg:hasSource`. |
| `mtg:CommanderDeckShape` (sibling) | An EDH deck MUST have one `mtg:hasCommander` and 99–100 `mtg:containsCard`. |
| `mtg:ConstructedDeckShape` (sibling) | A 60-card constructed deck MUST have ≥60 `mtg:containsCard`. |

If ingestion produced a malformed record, the SHACL validator throws
**before the triplestore ever sees it**. That's the entire purpose.

## What "decides the best deck" — line by line

The decision actually happens in
[`src/MtgDeckEngine.Graph/DeckRecommendationService.cs`](../src/MtgDeckEngine.Graph/DeckRecommendationService.cs)
in `BuildBudgetDeckAsync`. The algorithm:

### Step 1 — SPARQL fetches the candidate pool

`GetRecommendationsAsync` runs a SPARQL query that joins:

- EDHREC inclusion % + synergy (from the commander-scoped named graph)
- Tournament top-cut appearances (from the default graph)
- Card price + name + type + image URL (from global card data)

with these filters applied as `FILTER (?priceEur <= 5)` and similar.
The result is up to 500 candidate cards, **ranked by inclusion %**.

### Step 2 — C# greedy with type quotas

```csharp
// Default quotas for a Commander deck.
// Sum == 99 (the commander is the 100th).
new DeckBuildQuotas(
    Lands: 37, Ramp: 10, Draw: 10,
    Removal: 8, Creatures: 20, Other: 14);
```

Pass 1 — fill each bucket up to its target:

```
for each card in pool, in rank order:
    bucket = Categorise(card)           // by EDHREC category label
    limit  = QuotaFor(quotas, bucket)
    if taken[bucket] < limit
       AND running_total + card.price ≤ budget:
         add card; running_total += price
```

Pass 2 — top up to 99 with whatever scores highest under budget,
regardless of bucket.

That's the whole decision. **No SHACL involved at any step.**
The "best" notion here is operational, not formal:

| Signal | Source | Used as |
|---|---|---|
| Card popularity | EDHREC inclusion % | Primary rank |
| Tournament viability | Top-cut count | Optional secondary rank when `source=Tournament` |
| Cost ceiling | Per-card price + total budget | Hard filter |
| Deck shape | Quota table | Soft per-bucket cap |

## Where SHACL *could* legitimately help with deck-building

Three places, none of them implemented yet:

### 1. Validate that the *output* deck is structurally legal

After the greedy assembles 99 cards, we could run SHACL on the resulting
deck graph to confirm it satisfies `CommanderDeckShape` (correct counts,
exactly one commander). Today the greedy code has its own check
(`if (chosen.Count >= 99) break;`) — SHACL would replace that with a
declarative contract.

### 2. Detect format violations from imported data

A Modern deck imported via TopDeck.gg shouldn't have a `mtg:hasCommander`
property. A SHACL constraint `sh:not [ sh:path mtg:hasCommander ]` on
constructed-format decks would catch the cross-format bug at ingestion.

### 3. Gate "is this card legal in this format" via shape composition

`mtg:CommanderLegalShape` could say "every `mtg:containsCard` of a
Commander deck MUST have `mtg:isCommanderLegal true`". Today the mapper
asserts the boolean from Scryfall, but nothing checks the join.

These are the right uses of SHACL. **Nothing about picking, scoring, or
optimising belongs there.**

## Mental model: SHACL ↔ SPARQL ↔ C#

| Layer | Question it answers | When |
|---|---|---|
| **OWL** | What classes and properties *exist*? | Schema design, inference |
| **SHACL** | Is this specific graph *structurally valid*? | At write time (ingestion gate) |
| **SPARQL** | Which subgraph *matches* this pattern? | At query time (every API request) |
| **C#** | Of the matched subgraph, which is *best by this rule*? | After SPARQL, before HTTP response |

If you find yourself wanting SHACL to do "rank these cards by price", you
actually want SPARQL (`ORDER BY ?priceEur`) or C# (`pool.OrderBy(c => c.PriceEur)`).
SHACL only knows yes/no.

## Suggested exercises (close the loop)

1. **Read the existing greedy.** `BuildBudgetDeckAsync` in
   `DeckRecommendationService.cs` is ~25 lines. Trace one card from query
   result to "in the deck".
2. **Wire the validator in once.** In `CommanderIngestor.IngestCommanderAsync`,
   before `repo.WriteAsync(globalGraph, …)`, inject `ShaclValidator` and
   call `Validate(globalGraph)`. Run ingestion against a card whose
   `oracle_id` you've manually nulled in a debug build — you should see
   `ShaclValidationException` instead of a silent corrupt write.
3. **Write a new shape.** Add a `mtg:CardImageShape` that requires
   `mtg:hasImageUrl`. Run validation against the current store. Note how
   many of the 26,000+ tournament cards fail (only EDHREC + tournament-
   resolved cards have images — see the Phase 6c gap fix). That's
   diagnostic SHACL value.

## TL;DR

- **SHACL = "this data is well-formed."** No selection, no ranking.
- **The budget deck builder = greedy C# with type quotas.**
- **SPARQL is the bridge** — it pulls the candidate pool with prices,
  inclusion %, and tournament counts already joined.
- **Mixing these up is a recipe for confusion.** The W3C primers are
  blunt about the distinction; if you want a one-liner: *SHACL gates,
  SPARQL queries, code decides.*
