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
6. **(Phase 2)** Calls EDHTop16's GraphQL API for the same commanders to
   pull tournament aggregate stats (entry count, top-cut count, win rate,
   conversion rate, meta share) and per-entry tournament data (placement,
   wins/losses Swiss + bracket, decklist URL, full 99-card maindeck). All
   tournament triples land in the default graph — they're facts, not
   commander-scoped opinion data.

Expected log shape on success:

```
info: ... StartupIngestionWorker[0] Loading TBox + SHACL shapes into the triplestore
info: ... ScryfallBulkCache[0]      Scryfall bulk cache fresh — using local file.
info: ... ScryfallBulkCache[0]      Scryfall bulk cache loaded: 70,234 cards.
info: ... CommanderIngestor[0]      EDHREC: 246 entries
info: ... CommanderIngestor[0]      Scryfall cache resolved 240/246 cards (6 misses)
info: ... CommanderIngestor[0]      Wrote 1,872 global + 1,232 commander-scoped triples for xyris-the-writhing-storm
info: ... EdhTop16Ingestor[0]       EDHTop16: ingesting Xyris, the Writhing Storm (slug=xyris-the-writhing-storm)
info: ... EdhTop16Ingestor[0]       EDHTop16: stats — 38 entries, 7 top-cuts, win 19.3%, conv 18.4%, meta 0.04%
info: ... EdhTop16Ingestor[0]       EDHTop16: wrote 38 tournament entries for xyris-the-writhing-storm
info: ... StartupIngestionWorker[0] Startup ingestion complete.
```

### EDHTop16 ingestion notes

- **No auth required.** The endpoint is `https://edhtop16.com/api/graphql`.
- The mapper joins by `Card.oracleId`, which matches Scryfall's identifier
  exactly — no name resolution needed for tournament cards.
- Toggle off with `"Ingestion": { "EnableEdhTop16": false }` in
  `appsettings.json` / `appsettings.Development.json` if you want a
  Phase-1-only run.
- Tournament data window is the EDHTop16 default (last six months at time
  of ingestion). Tweak the `TimePeriod` enum in `EdhTop16Ingestor.cs` if
  you want a different window.

### TopDeck.gg ingestion (Phase 3 — multi-format)

TopDeck.gg covers EDH **and** Modern, Pioneer, Legacy, Standard, Pauper, and
more from a single REST endpoint with full decklists and round data.
Disabled by default because it requires a free API key.

**Enable it:**

1. Register at https://topdeck.gg, then generate an API key in your
   profile.
2. Store the key with **.NET user-secrets** (preferred — keys live in
   your home directory, never anywhere near git):
   ```bash
   dotnet user-secrets set "TopDeck:ApiKey" "your-key-here" \
     --project src/MtgDeckEngine.Api
   dotnet user-secrets set "Ingestion:EnableTopDeck" "true" \
     --project src/MtgDeckEngine.Api
   ```
   Optional tuning (defaults are fine for most setups):
   ```bash
   dotnet user-secrets set "TopDeck:Formats:0" "EDH"      --project src/MtgDeckEngine.Api
   dotnet user-secrets set "TopDeck:Formats:1" "Modern"   --project src/MtgDeckEngine.Api
   dotnet user-secrets set "TopDeck:LookbackDays" "60"    --project src/MtgDeckEngine.Api
   dotnet user-secrets set "TopDeck:MinParticipants" "16" --project src/MtgDeckEngine.Api
   ```
   *(Alternative: drop the same settings into `appsettings.Local.json`,
   which is gitignored. Use user-secrets if you can — it's the .NET idiom
   and keeps the API project free of secret-shaped files.)*
3. Restart the API. You'll see lines like:
   ```
   info: TopDeckIngestor[0] TopDeck: fetching EDH tournaments, last 60 days, ≥16 players
   info: TopDeckIngestor[0] TopDeck: wrote 42 EDH tournaments, 1,847 entries; 31 card-name misses
   info: TopDeckIngestor[0] TopDeck: fetching Modern tournaments, last 60 days, ≥16 players
   info: TopDeckIngestor[0] TopDeck: wrote 18 Modern tournaments, 612 entries; 8 card-name misses
   ```

**Multi-format ontology.** `mtg:hasFormat` lives on both Tournament and Deck;
TopDeck normalises format strings to uppercase + underscores (`EDH`, `MODERN`,
`DUEL_COMMANDER`, ...). The SHACL `DeckShape` was relaxed in Phase 3 so
60-card constructed decks pass — Commander-specific (99–100 card) and
Constructed-specific (60+) shapes ship as optional sibling shapes that can be
enabled selectively when SHACL validation is wired into the ingest pipeline.

**Attribution.** TopDeck.gg's terms require visible credit linking back to
their site for any project that uses the API. The README's roadmap section
includes this credit; keep it there if you fork.

---

## API endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/health` | Liveness probe |
| `GET` | `/api/commanders/{slug}/recommendations` | Ranked cards by EDHREC + tournament signals with filters |
| `GET` | `/api/commanders/{slug}/meta` | Tournament-derived commander signals (Phase 2) |
| `GET` | `/api/commanders/{slug}/build-deck` | Greedy budget deck builder |

### Examples

#### Phase 1 — EDHREC-driven recommendations

```bash
# Top 15 non-land cards under €5
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?maxPriceEur=5&excludeLands=true&limit=15' | jq

# Only EDHREC "High Synergy Cards" — discover the archetype glue
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?includeOnlyCategories=High%20Synergy%20Cards&limit=10' | jq

# Greedy €100 deck, no single card over €15
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/build-deck?totalBudgetEur=100&maxCardPriceEur=15' | jq
```

#### Phase 2 — tournament-aware recommendations + meta

```bash
# DoD query: cards in Top 4 Xyris decks under €5
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?source=Tournament&maxPlacement=4&maxPriceEur=5&excludeBasicLands=true&limit=15' | jq

# All-source ranking: must appear in ≥2 top-cut decks AND fit a €10 budget,
# sorted by tournament count then EDHREC inclusion
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/recommendations?source=All&minTopCutAppearances=2&maxPriceEur=10&excludeBasicLands=true&limit=15' | jq

# Commander tournament meta — entry count, top-cut count, win/conversion/meta-share
curl 'http://localhost:5050/api/commanders/xyris-the-writhing-storm/meta' | jq
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
| `source` | `Edhrec` / `Tournament` / `All` | `All` | Which signal to rank by. `Tournament` requires `MaxPlacement`/`MinTopCutAppearances` to mean anything. `All` ranks by tournament count first, then EDHREC inclusion. |
| `maxPlacement` | int | none | Only count tournament entries with placement ≤ this when scoring/filtering |
| `minTopCutAppearances` | int | none | Only return cards that appeared in ≥ N qualifying top-cut decks |

### Response shape — recommendations

Each returned card now includes `topCutAppearances` (Phase 2 addition):

```json
{
  "oracleId":  "...",
  "name":      "Windfall",
  "category":  "High Synergy Cards",
  "inclusionPct": 63.10,
  "synergyScore": 0.54,
  "priceEur": 4.88,
  "topCutAppearances": 5
}
```

`topCutAppearances` is `null` for EDHREC-source queries (we don't compute it
there) and a count for Tournament/All sources.

### Response shape — meta

```json
{
  "commanderSlug": "xyris-the-writhing-storm",
  "tournamentEntryCount": 38,
  "topCutCount": 7,
  "winRate": 0.1925,
  "conversionRate": 0.1842,
  "metaShare": 0.000418,
  "top4DeckCount": 3,
  "top16DeckCount": 7,
  "latestTopCutDate": "2026-04-25"
}
```

---

## Running raw SPARQL

The `queries/` folder holds named, version-controlled SPARQL queries. Run any
of them with the included runner:

```bash
# EDHREC-flavoured queries (Phase 1)
./bin/sparql queries/01-top-by-inclusion.sparql
./bin/sparql queries/03-high-synergy.sparql
./bin/sparql queries/07-category-histogram.sparql

# Tournament-flavoured queries (Phase 2)
./bin/sparql queries/13-tournament-meta.sparql            # commander aggregate stats
./bin/sparql queries/14-top-cards-in-top4.sparql          # DoD query
./bin/sparql queries/15-popularity-vs-performance.sparql  # EDHREC favourites with zero top-cut presence
./bin/sparql queries/16-recent-top-decks.sparql           # latest top-cut Xyris entries with dates
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
- **Phase 2 — Tournament signals (done — EDHTop16).** Tournament aggregate
  stats + per-entry data via EDHTop16 GraphQL; `/meta` endpoint;
  tournament-aware recommendations (`source=Tournament` / `All`,
  `maxPlacement`, `minTopCutAppearances`).
- **Phase 3 — Multi-format (done — TopDeck.gg).** REST ingestion of
  tournament data across every TopDeck-supported format (EDH, Modern,
  Pioneer, Legacy, Standard, Pauper, …). Multi-format ontology + relaxed
  SHACL shapes. Feature-flagged behind a free API key.
- **Phase 4 — Format endpoints + smarter builder + AI (done).**
  `/api/formats`, `/api/formats/{format}/meta`,
  `/api/formats/{format}/staples`, `/api/commanders` (list).
  Quota-aware budget deck builder (Lands 37 · Ramp 10 · Draw 10 ·
  Removal 8 · Creatures 20 · Other 14). `POST /api/commanders/{slug}/ai/suggest`
  (Anthropic, prompt-cached, feature-flagged).
- **Phase 5 — AWS deployment (done — infra-as-code).** `FusekiOptions`
  documented to work against Neptune unchanged (SPARQL 1.1 wire-compatible);
  `k8s/` Kustomize layout (base + dev/prod overlays); `deploy/helm/`
  Helm chart with IRSA / ConfigMap / Secret / HPA / Ingress / nightly
  CronJob; `terraform/` modules + envs for VPC + EKS + Neptune + IRSA.
  Apply with `terraform apply` then `helm install`.

> Tournament data sourced in part from [TopDeck.gg](https://topdeck.gg).

See [CLAUDE.md](CLAUDE.md) for the long-form plan, ontology, and interview-ready
notes on every architectural choice.

---

## License

Personal portfolio project. License TBD before any wider distribution.
