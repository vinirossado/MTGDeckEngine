# MTG Deck Intelligence Engine

A .NET 10 knowledge-graph engine that aggregates Magic: The Gathering deck data
(EDHREC, Scryfall, and — coming soon — EDHTop16 / Spicerack) into a single RDF
triplestore, and exposes a SPARQL-powered query API for deck recommendations,
budget filtering, and competitive performance insights.

**Why it exists.** EDHREC shows *popularity*. EDHTop16 shows *tournament
performance*. They are different signals. By fusing them in one knowledge graph
you can ask questions no single source can answer:

> *"What are the most tournament-winning cards in Xyris decks that cost under
> €5?"*

Initially scoped to the Commander format and the commander **Xyris, the
Writhing Storm**.

---

## Architecture

```
┌──────────────────┐    HTTP     ┌────────────────────┐   SPARQL    ┌───────────┐
│  EDHREC          │ ─────────▶ │  MtgDeckEngine.Api │ ──────────▶ │  Fuseki   │
│  Scryfall (bulk) │            │  + Ingestion       │ ◀────────── │  (TDB2)   │
└──────────────────┘            └────────────────────┘    JSON     └───────────┘
```

The .NET 10 solution is split into five small projects + two test projects:

| Project | Purpose |
|---|---|
| `MtgDeckEngine.Core` | Domain DTOs + interfaces (`IGraphRepository`, `IDeckRecommendationService`) |
| `MtgDeckEngine.Ontology` | OWL ontology + SHACL shapes, embedded as resources |
| `MtgDeckEngine.Graph` | dotNetRDF wrappers — Fuseki + in-memory repos, SPARQL queries |
| `MtgDeckEngine.Ingestion` | EDHREC + Scryfall clients, bulk cache, ingestion workers |
| `MtgDeckEngine.Api` | ASP.NET Core 10 Web API |
| `tests/*` | xUnit unit + integration tests |

Triplestore: **Apache Jena Fuseki** locally (TDB2-backed Docker container).
Production target: **Amazon Neptune** (same SPARQL 1.1 protocol; swap only the
endpoint URL).

For the full architectural rationale, the ontology design, and the longer-term
roadmap, see [CLAUDE.md](CLAUDE.md).

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ | Required to build and run the API |
| [Docker](https://docs.docker.com/get-docker/) | latest | Runs Fuseki and the API image |
| [`jq`](https://stedolan.github.io/jq/) | any | Used by the SPARQL runner script (`bin/sparql`) |
| [Zed](https://zed.dev) (optional) | latest | Project ships with `.zed/tasks.json` for one-click query runs |

---

## Quick start

```bash
# 1. Boot Fuseki (in-Docker TDB2 store at http://localhost:3030/mtg)
docker compose up -d fuseki

# 2. Run the API — kicks off ingestion on startup
dotnet run --project src/MtgDeckEngine.Api

# 3. Once you see "Startup ingestion complete." in the logs:
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?maxPriceEur=5&excludeBasicLands=true&limit=10' | jq
```

That's the whole loop. The API runs on **http://localhost:5050** and Fuseki's
admin UI is at **http://localhost:3030**.

### First-run ingestion (one-time)

The ingestion worker:

1. Loads the OWL/SHACL ontology into the default graph.
2. **Downloads Scryfall's bulk card file** (~250 MB JSON) on first start. Cached
   locally at `~/Library/Application Support/MtgDeckEngine/scryfall-default-cards.json`
   (macOS) or the platform equivalent. Refreshed if older than 7 days.
3. Fetches the EDHREC page for each configured commander (default:
   `xyris-the-writhing-storm`).
4. Resolves card names → oracle IDs via the bulk cache (no per-card HTTP calls).
5. Writes global card triples to the default graph and EDHREC inclusion /
   synergy / category triples to a commander-scoped named graph
   (`http://example.org/mtg#context/{slug}`).

Expected log shape on success:

```
info: ... StartupIngestionWorker[0] Loading TBox + SHACL shapes into the triplestore
info: ... ScryfallBulkCache[0]      Scryfall bulk cache fresh — using local file.
info: ... ScryfallBulkCache[0]      Scryfall bulk cache loaded: 70,234 cards.
info: ... CommanderIngestor[0]      EDHREC: 246 entries
info: ... CommanderIngestor[0]      Scryfall cache resolved 240/246 cards (6 misses)
info: ... CommanderIngestor[0]      Wrote 1,872 global + 1,232 commander-scoped triples for xyris-the-writhing-storm
info: ... StartupIngestionWorker[0] Startup ingestion complete.
```

---

## API endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/health` | Liveness probe |
| `GET` | `/api/commanders/{slug}/recommendations` | Ranked cards by EDHREC inclusion + synergy with filters |
| `GET` | `/api/commanders/{slug}/build-deck` | Greedy budget deck builder |

### Examples

```bash
# Top 15 non-land cards under €5
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?maxPriceEur=5&excludeLands=true&limit=15' | jq

# Only EDHREC "High Synergy Cards" — discover the archetype glue
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?includeOnlyCategories=High%20Synergy%20Cards&limit=10' | jq

# Greedy €100 deck, no single card over €15
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/build-deck?totalBudgetEur=100&maxCardPriceEur=15' | jq

# Exclude lands + a couple of categories
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?excludeLands=true&excludeCategories=Top%20Cards,Mana%20Artifacts&limit=15' | jq
```

### Recommendation query params

| Param | Type | Default | Meaning |
|---|---|---|---|
| `maxPriceEur` | decimal | none | Drop cards above this single-card price |
| `minInclusion` | decimal | 0 | Min EDHREC inclusion % |
| `minSynergy` | decimal | none | Min EDHREC synergy score |
| `excludeLands` | bool | false | Skip all lands |
| `excludeBasicLands` | bool | true | Skip Plains/Island/Swamp/Mountain/Forest |
| `excludeCategories` | csv string | none | EDHREC category labels to drop |
| `includeOnlyCategories` | csv string | none | Restrict to these EDHREC categories |
| `limit` | int | 50 | Cap on returned rows (max 500) |

---

## Running raw SPARQL

The `queries/` folder holds named, version-controlled SPARQL queries. Run any
of them with the included runner:

```bash
./bin/sparql queries/01-top-by-inclusion.sparql
./bin/sparql queries/03-high-synergy.sparql
./bin/sparql queries/07-category-histogram.sparql
```

Override the endpoint with `SPARQL_ENDPOINT=https://... ./bin/sparql <file>` or
pass it as the second positional arg.

### From Zed

`.zed/tasks.json` defines:

- **Run SPARQL: active file** — runs whatever `.sparql` file you have focused.
- **Count triples**, **Top cards by inclusion** — fixed shortcuts.
- **API: Xyris recommendations**, **API: Build €100 Xyris deck** — fire the
  HTTP endpoints from the task picker.

Bind to a keystroke in `~/.config/zed/keymap.json`:

```json
{
  "context": "Workspace",
  "bindings": { "cmd-r": ["task::Spawn", { "task_name": "Run SPARQL: active file" }] }
}
```

### From the Fuseki UI

Open http://localhost:3030, click the `mtg` dataset → **query** tab, and paste
any `.sparql` file content. Great for ad-hoc exploration and the result table
has sortable columns.

---

## Tests

```bash
dotnet test
```

Phase 1 ships 4 unit tests covering the in-memory repository, the Scryfall
mapper, and the deck-recommendation service.

---

## Project layout

```
MtgDeckEngine/
├── CLAUDE.md                    ← Full architecture + roadmap doc
├── README.md                    ← (this file)
├── docker-compose.yml
├── docker/fuseki/               ← Custom Fuseki image (Apache Jena 5.1)
├── src/
│   ├── MtgDeckEngine.Core/
│   ├── MtgDeckEngine.Ontology/
│   ├── MtgDeckEngine.Graph/
│   ├── MtgDeckEngine.Ingestion/
│   └── MtgDeckEngine.Api/
├── tests/
│   ├── MtgDeckEngine.UnitTests/
│   └── MtgDeckEngine.IntegrationTests/
├── queries/                     ← Versioned SPARQL queries
├── bin/sparql                   ← Runner script for queries/
└── .zed/                        ← Zed tasks + project settings
```

---

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| API can't reach Fuseki (`503 Service Unavailable`) | Fuseki container not up yet — `docker compose logs fuseki \| tail -20`. |
| Port 5000 returns AirTunes responses (macOS) | macOS AirPlay Receiver hogs `:5000`. Project uses `:5050` to avoid it. |
| Scryfall returns `429 Too Many Requests` during ingestion | You hit Scryfall's rate limit before the bulk cache was in place. Wait 5–10 min and retry — the bulk cache means it should only ever happen once. |
| `inclusion` values look like 18,000+ | EDHREC's raw `inclusion` field is the deck count. The mapper converts to `100 * num_decks / potential_decks`. If you see raw counts you're on an outdated ingest — drop the named graph and re-ingest. |

Drop the commander-scoped graph to force a clean re-ingest:

```bash
curl -X POST 'http://localhost:3030/mtg/update' \
  -H 'Content-Type: application/sparql-update' \
  --data 'DROP GRAPH <http://example.org/mtg#context/xyris-the-writhing-storm>'
```

---

## Roadmap

- **Phase 1 — MVP (done).** EDHREC + Scryfall ingestion, recommendation API,
  budget deck builder.
- **Phase 2 — Tournament signals.** EDHTop16 + Spicerack ingestion; "cards in
  Top 4 Xyris decks under €5" queries.
- **Phase 3 — Multi-commander + AI layer.** Generalised ingestion across any
  commander slug; Moxfield decklists; LLM-backed deck-suggestion endpoint.
- **Phase 4 — AWS.** Swap Fuseki for Amazon Neptune (same SPARQL 1.1
  protocol); deploy API to EKS; background workers as CronJobs;
  CloudWatch / X-Ray observability.

See [CLAUDE.md](CLAUDE.md) for the long-form plan, ontology, and interview-ready
notes on every architectural choice.

---

## License

Personal portfolio project. License TBD before any wider distribution.
