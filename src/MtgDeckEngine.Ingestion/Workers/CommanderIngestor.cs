using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Ingestion.Http;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Ingestion.Mappers;
using VDS.RDF;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.Ingestion.Workers;

/// <summary>
/// Pulls EDHREC's commander page, resolves each card via Scryfall for stable
/// oracle ids + prices, then writes:
///  - global card triples to the default graph
///  - EDHREC signal triples to a named graph scoped to the commander.
/// </summary>
public sealed class CommanderIngestor(
    EdhrecClient edhrec,
    ScryfallClient scryfall,
    ScryfallBulkCache scryfallCache,
    IGraphRepository repo,
    ILogger<CommanderIngestor> logger) : IIngestorService
{
    public string SourceName => "edhrec+scryfall";

    public async Task IngestAsync(CancellationToken ct) =>
        await IngestCommanderAsync("xyris-the-writhing-storm", maxCards: 250, delayMs: 100, ct).ConfigureAwait(false);

    public async Task IngestCommanderAsync(
        string slug,
        int maxCards,
        int delayMs,
        CancellationToken ct)
    {
        logger.LogInformation("Ingesting commander {Slug}", slug);
        var page = await edhrec.GetCommanderPageAsync(slug, ct).ConfigureAwait(false);
        if (page is null)
        {
            logger.LogWarning("EDHREC returned no data for {Slug}", slug);
            return;
        }

        var entries = EdhrecToRdfMapper.ToEntries(page, slug)
            .OrderByDescending(e => e.InclusionPct)
            .Take(maxCards)
            .ToList();
        logger.LogInformation("EDHREC: {Count} entries", entries.Count);

        await scryfallCache.EnsureLoadedAsync(ct).ConfigureAwait(false);

        var distinctNames = entries.Select(e => e.Name).Distinct().ToList();
        var nameToOracleId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var globalGraph = new RdfGraph();
        var hits = 0; var misses = 0;

        foreach (var name in distinctNames)
        {
            ct.ThrowIfCancellationRequested();
            if (!scryfallCache.TryGetByName(name, out var sc) || sc.OracleId is null)
            {
                misses++;
                logger.LogDebug("Scryfall cache miss for {Name}", name);
                continue;
            }
            var dto = ScryfallToRdfMapper.ToDto(sc);
            ScryfallToRdfMapper.AssertCard(globalGraph, dto);
            nameToOracleId[name] = dto.OracleId;
            hits++;
        }
        logger.LogInformation("Scryfall cache resolved {Hits}/{Total} cards ({Misses} misses)",
            hits, distinctNames.Count, misses);

        // The commander itself. Resolved through the slug index rather than by
        // un-slugifying the name: that round-trip is lossy and never matched a
        // double-faced card, whose printed name is "Front // Back".
        //
        // Asserting mtg:commander/{slug} here is what makes an ingested
        // commander visible at all — the commander list keys on that node, and
        // nothing else on the EDHREC path was creating it, so a commander you
        // ingested by name simply never showed up.
        if (scryfallCache.TryGetBySlug(slug, out var commanderCard)
            && commanderCard.OracleId is not null)
        {
            var dto = ScryfallToRdfMapper.ToDto(commanderCard);
            ScryfallToRdfMapper.AssertCard(globalGraph, dto);
            ScryfallToRdfMapper.AssertCommanderNode(globalGraph, slug, dto);
            nameToOracleId[commanderCard.Name] = dto.OracleId;
        }
        else
        {
            logger.LogWarning(
                "Could not resolve commander card for slug {Slug}; it will not appear in the commander list.",
                slug);
        }
        _ = delayMs; // no longer needed — cache lookups are local
        _ = scryfall; // kept on the type for future incremental lookups

        // Prices drift, so drop the previous snapshot before writing the new
        // one — otherwise both survive and MIN(?price) keeps quoting the old value.
        await MutableProperties
            .ClearAsync(repo, globalGraph, MutableProperties.Card, ct)
            .ConfigureAwait(false);
        await repo.WriteAsync(globalGraph, namedGraphUri: null, ct).ConfigureAwait(false);

        var contextGraph = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(contextGraph, entries, nameToOracleId);
        var contextUri = new Uri(MtgVocab.CommanderContextUri(slug));

        // Snapshot semantics: EDHREC inclusion %, synergy, etc. drift with
        // every meta update. Drop the old graph before writing the new one
        // so we don't accumulate near-duplicate triples (e.g. inclusion 74.58
        // alongside 74.57) that would surface as visible duplicates in queries.
        await repo.DropGraphAsync(contextUri, ct).ConfigureAwait(false);
        await repo.WriteAsync(contextGraph, contextUri, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Wrote {GlobalTriples} global + {CtxTriples} commander-scoped triples for {Slug}",
            globalGraph.Triples.Count, contextGraph.Triples.Count, slug);
    }

    // EDHREC slug → display name; coarse but works for the seed Phase 1 commanders.
    // The Scryfall round-trip below corrects the canonical name via Name field.
    private static string SlugToCommanderName(string slug)
    {
        var parts = slug.Split('-');
        return string.Join(" ", parts.Select(p => p.Length > 0
            ? char.ToUpperInvariant(p[0]) + p[1..]
            : p));
    }
}
