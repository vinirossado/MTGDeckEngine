# Queries the engine actually runs

Snapshots of the SPARQL the API issues, one runnable query per file, captured
with real parameters so each executes as-is:

```bash
bin/sparql queries/engine/01-recommendations-tournament-under-5eur.sparql
```

Unlike the hand-written files in `queries/`, these are **generated**. They will
drift as the query builders change — regenerate rather than hand-editing:

```bash
bin/explain '/api/commanders/xyris-the-writhing-storm/build-deck?totalBudgetEur=600&maxBracket=3' --save out/
```

`explain=true` works on any of `/recommendations`, `/build-deck`, `/meta` and
`/discover`, and always reflects the parameters you pass — the budget and
filters below are baked into these particular snapshots.

| File | Endpoint | Parameters captured |
|---|---|---|
| `01-recommendations-tournament-under-5eur` | `/recommendations` | Xyris, ≤€5, tournament source, limit 25 |
| `02-recommendations-edhrec-staples` | `/recommendations` | Xyris, ≥25% inclusion, no basics |
| `03-deck-builder-card-pool` | `/build-deck` | Xyris, €600, bracket ≤3 — the candidate pool |
| `04-deck-builder-basic-lands` | `/build-deck` | Oracle ids and art for the manabase |
| `05-commander-meta-aggregate` | `/meta` | EDHTop16 commander aggregates |
| `06-commander-meta-derived` | `/meta` | Fallback computed from ingested entries |
| `07-commander-discovery` | `/discover` | Bracket ≤3, ≤€200, ≥3 decks |

## What these queries do *not* show

Reading them as "how the engine decides" will mislead you. Three things happen
outside SPARQL:

1. **Ranking is C#.** The pool query returns rows ordered by wins and inclusion;
   the actual ranking applies a shrunk win-rate blend (cards) or a Wilson lower
   bound (commanders). Sorting these results by `winRate` will not reproduce the
   API's order.
2. **Budget packing is C#.** The greedy complete-then-upgrade knapsack, the
   bracket cap and the combo repair all operate on the returned rows.
3. **The Commander Bracket comes from Commander Spellbook over HTTP**, not the
   graph, so it appears in no query here.

## Things worth reading closely

- **Grouped subqueries** (`03`): price, image, colour identity and category are
  each fetched in their own `GROUP BY` subquery rather than joined inline. A
  card carries several of each, and an inline `OPTIONAL` multiplies the outer
  rows — `LIMIT 500` would then return a handful of distinct cards instead of
  500. This is the single most instructive pattern in the set.
- **`MIN|MAX` grouping key** (`07`): partner pairs are grouped by the *pair*,
  keyed on sorted slugs so the same pair cannot split into `a+b` and `b+a`.
- **Per-event top cut** (`06`): `?placement <= COALESCE(?cutSize, 16)` — placing
  16th in a 20-player event is not a conversion.
- **Price fallback** (`01`, `03`): `COALESCE(?eurMin, ?usdMin)`, because many
  cards have no EUR price at all.
