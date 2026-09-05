# Benchmarks

Measured 2026-09-05 on the local dev machine (Apple Silicon, Docker Desktop),
against Apache Jena Fuseki 5.1 with `JVM_ARGS=-Xmx2g`, TDB2-backed.

Everything below was produced by running the code in this repository — the
write numbers go through `FusekiGraphRepository.WriteAsync`, so they include
the 8 MB batching, not a synthetic loader.

## Dataset

| | |
|---|---|
| Triples (default graph) | 1,859,635 |
| `mtg:containsCard` edges | 1,312,964 |
| Named graphs | 2 (one per commander context, EDHREC popularity) |
| Cards | 14,958 (12,882 with a price) |
| Decks | 13,311 |
| Tournament entries | 27,393 |
| Commanders | 774 |
| Serialised size (N-Triples) | 314.9 MB |

## Build and tests

| | |
|---|---|
| `dotnet build -c Release` | 3.6 s, 0 warnings, 0 errors |
| `dotnet test -c Release` | **185 passed**, 0 failed, 3 s |

`MtgDeckEngine.IntegrationTests` contains no tests — the project exists but is
empty.

## Write throughput

Real `mtg:containsCard` triples exported from the live dataset, then written
back through `FusekiGraphRepository.WriteAsync` into a throwaway in-memory
Fuseki. Parse is dotNetRDF's `NTriplesParser`; write is the batched
`INSERT DATA` path.

| Triples | Size | Parse | Write | UPDATE requests | Triples/s |
|--------:|-----:|------:|------:|----------------:|----------:|
| 50,000 | 8.8 MB | 0.84 s | 1.68 s | 2 | 29,809 |
| 200,000 | 35.1 MB | 0.89 s | 4.66 s | 5 | 42,887 |
| **1,859,635** | **314.9 MB** | **13.31 s** | **46.92 s** | **40** | **39,631** |

Throughput plateaus around **40k triples/s**. The full graph takes just under
a minute of wall-clock write time and is split into **40 HTTP requests**, which
the repository logs:

```
SPARQL UPDATE split into 40 requests (1859635 triples) → (default)
```

### Why 40 requests and not 1

`MaxBytesPerUpdate` is 8 MB. From the source comment:

> Fuseki posts SPARQL Update as a form, and Jetty rejects form bodies over
> 20 MB with "form too large" — surfaced to the client as a bare HTTP 500.

8.8 MB of N-Triples is ~50,000 triples, so the cap is reached at roughly
46k triples per request.

## Ingestion (one commander)

`POST /api/commanders/{slug}/ingest` against an empty throwaway Fuseki, with
the Scryfall bulk cache already warm on disk. Duration is the API's own
`IngestSummary.durationMs`.

| Run | Commander | Graph state | Duration | EDHREC | Scryfall cache | Tournament entries |
|---|---|---|---:|---:|---|---:|
| 1 | xyris-the-writhing-storm | empty | **8.32 s** | 250 | 247/249 hit | 25 |
| 2 | xyris-the-writhing-storm | already ingested | **4.83 s** | 250 | 247/249 hit | 25 |
| 3 | kefka-court-mage | empty for this slug | **15.33 s** | 250 | 237/248 hit | 100 |

Result: 23,033 triples for two commanders (20,565 default + 2,468 named).

### Ingestion is network-bound, not graph-bound

Run 1 wrote 7,583 triples. At the measured ~40k triples/s that is **~0.19 s of
writing inside an 8.32 s call** — roughly 2%. The other 98% is HTTP to EDHREC
and EDHTop16.

Two consequences visible in the numbers above:

- **Re-ingesting is faster than first ingest** (4.83 s vs 8.32 s) even though
  it does strictly more work — it adds the `MutableProperties.ClearAsync`
  DELETE. The bulk cache is warm in-process by then, so the saving is all
  local lookup, not graph work.
- **The spread tracks tournament entries, not triples.** Kefka takes 15.33 s
  against Xyris's 8.32 s because EDHTop16 returned 671 entries for Kefka
  (100 ingested, the `MaxTournamentEntriesPerCommander` cap) versus 69 for
  Xyris (25 ingested). Same triple volume, double the wall clock.

The triplestore is nowhere near being the bottleneck at this scale.

### Cold start

The Scryfall bulk cache (`scryfall-default-cards.json`, **595 MB**) has a
7-day TTL. When it is stale or missing, the first ingest of the week also
downloads it. The runs above deliberately used a warm cache, so that download
is *not* included in the times.

## Query latency

All 19 versioned queries in `queries/`, p50 of 9 runs after 2 warm-up runs,
measured over HTTP against the loaded dataset.

| Query | p50 | min | max | Rows |
|---|---:|---:|---:|---:|
| 10-schema-overview | 2 ms | 2 | 3 | 7 |
| 12-card-by-name | 2 ms | 2 | 2 | 11 |
| 13-tournament-meta | 2 ms | 2 | 2 | 1 |
| 04-by-category | 3 ms | 3 | 4 | 0 |
| 03-high-synergy | 3 ms | 3 | 3 | 2 |
| 07-category-histogram | 3 ms | 2 | 4 | 12 |
| 08-price-distribution | 3 ms | 3 | 4 | 5 |
| 09-popularity-vs-cost | 3 ms | 2 | 3 | 2 |
| 18-modern-staples | 3 ms | 3 | 4 | 25 |
| 05-named-graphs | 5 ms | 4 | 7 | 2 |
| 01-top-by-inclusion | 6 ms | 5 | 7 | 25 |
| 02-non-land-under-5eur | 6 ms | 5 | 7 | 25 |
| 06-swap-suggestions | 14 ms | 13 | 15 | 10 |
| 14-top-cards-in-top4 | 39 ms | 38 | 41 | 25 |
| 17-formats-overview | 47 ms | 46 | 49 | 3 |
| 16-recent-top-decks | 52 ms | 51 | 53 | 11 |
| 00-count-triples | 217 ms | 216 | 220 | 1 |
| 11-predicates-used | 324 ms | 321 | 328 | 30 |
| **15-popularity-vs-performance** | **613 ms** | 609 | 619 | 25 |

**16 of 19 queries answer in under 55 ms.** The three slow ones are the
informative ones:

- `00-count-triples` (217 ms) — a full count of 1.86M triples. This is the
  floor for anything that touches every triple.
- `11-predicates-used` (324 ms) — full scan with `GROUP BY ?p`.
- `15-popularity-vs-performance` (613 ms) — the only query that joins a
  **named graph** against the **default graph** and wraps the tournament side
  in `OPTIONAL`. Nearly 3× the cost of counting the entire dataset, for
  25 rows. Cross-graph joins are the expensive shape here, not data volume.

`04-by-category` parses and runs but returns 0 rows — either a data gap or a
stale query. Worth a look.

## Reproducing

```bash
docker compose up -d fuseki          # or: docker start mtg-fuseki
dotnet build -c Release
dotnet test  -c Release

# query latency
for f in queries/*.sparql; do
  curl -s http://localhost:3030/mtg/sparql \
    --data-urlencode "query=$(cat $f)" -H 'Accept: text/csv' -o /dev/null \
    -w "$f %{time_total}s\n"
done
```
