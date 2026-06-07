using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Ingestion.Http;
using MtgDeckEngine.Ingestion.Mappers;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.Ingestion.Workers;

/// <summary>
/// Fetches commander stats + tournament entries from EDHTop16's GraphQL API and
/// asserts them into the default graph.
/// </summary>
public sealed class EdhTop16Ingestor(
    EdhTop16Client edhtop,
    ScryfallBulkCache scryfall,
    IGraphRepository repo,
    ILogger<EdhTop16Ingestor> logger) : IIngestorService
{
    public string SourceName => "edhtop16";

    public Task IngestAsync(CancellationToken ct) =>
        IngestCommanderAsync("xyris-the-writhing-storm", maxEntries: 100, ct);

    public async Task IngestCommanderAsync(string slug, int maxEntries, CancellationToken ct)
    {
        var displayName = await ResolveDisplayNameAsync(slug, ct).ConfigureAwait(false);
        if (displayName is null)
        {
            logger.LogWarning("EDHTop16: could not resolve display name for {Slug}", slug);
            return;
        }

        logger.LogInformation("EDHTop16: ingesting {Display} (slug={Slug})", displayName, slug);

        // Stats — one query.
        var commander = await edhtop.GetCommanderStatsAsync(
            displayName, EdhTop16TimePeriod.SIX_MONTHS, ct).ConfigureAwait(false);

        var statsGraph = new RdfGraph();
        if (commander is not null)
        {
            EdhTop16ToRdfMapper.AssertCommanderStats(statsGraph, slug, commander);
            await repo.WriteAsync(statsGraph, namedGraphUri: null, ct).ConfigureAwait(false);
            logger.LogInformation(
                "EDHTop16: stats — {Count} entries, {TopCuts} top-cuts, win {Win:P1}, conv {Conv:P1}, meta {Meta:P2}",
                commander.Stats?.Count, commander.Stats?.TopCuts,
                commander.Stats?.WinRate, commander.Stats?.ConversionRate, commander.Stats?.MetaShare);
        }

        // Entries — paginated. Each page becomes one INSERT DATA batch.
        var entryGraph = new RdfGraph();
        var seen = 0;
        await foreach (var entry in edhtop.StreamEntriesAsync(displayName, pageSize: 25, ct))
        {
            ct.ThrowIfCancellationRequested();
            EdhTop16ToRdfMapper.AssertEntry(entryGraph, slug, entry);
            seen++;
            if (seen >= maxEntries) break;

            // Flush every 25 entries to keep the SPARQL UPDATE small.
            if (seen % 25 == 0)
            {
                await repo.WriteAsync(entryGraph, namedGraphUri: null, ct).ConfigureAwait(false);
                entryGraph = new RdfGraph();
            }
        }
        if (entryGraph.Triples.Count > 0)
            await repo.WriteAsync(entryGraph, namedGraphUri: null, ct).ConfigureAwait(false);

        logger.LogInformation("EDHTop16: wrote {Entries} tournament entries for {Slug}", seen, slug);
    }

    /// <summary>
    /// EDHTop16's GraphQL takes the commander's display name as it appears on
    /// Scryfall (e.g. "Xyris, the Writhing Storm"). Our slug is kebab-cased
    /// and slug → name is lossy (commas + case are gone), so we round-trip
    /// through the Scryfall bulk cache's slug index — which is built once at
    /// load time and is the authoritative reverse mapping.
    /// </summary>
    private async Task<string?> ResolveDisplayNameAsync(string slug, CancellationToken ct)
    {
        await scryfall.EnsureLoadedAsync(ct).ConfigureAwait(false);
        return scryfall.TryGetBySlug(slug, out var card) ? card.Name : null;
    }
}
