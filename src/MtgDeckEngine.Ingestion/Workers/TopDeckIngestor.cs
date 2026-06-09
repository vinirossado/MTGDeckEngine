using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Http;
using MtgDeckEngine.Ingestion.Mappers;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.Ingestion.Workers;

/// <summary>
/// Pulls completed tournaments from TopDeck.gg for each configured format and
/// writes Tournament / TournamentEntry / Deck triples into the default graph.
/// Tournament data is multi-format (Commander, Modern, Legacy, ...) — the
/// format propagates into <c>mtg:hasFormat</c> on both the Tournament and the Deck.
/// </summary>
public sealed class TopDeckIngestor(
    TopDeckClient client,
    ScryfallBulkCache scryfall,
    IGraphRepository repo,
    IOptions<TopDeckOptions> options,
    ILogger<TopDeckIngestor> logger) : IIngestorService
{
    public string SourceName => "topdeck";

    public Task IngestAsync(CancellationToken ct) => IngestAllFormatsAsync(ct);

    public async Task IngestAllFormatsAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            logger.LogWarning("TopDeck: ApiKey not configured; skipping.");
            return;
        }

        await scryfall.EnsureLoadedAsync(ct).ConfigureAwait(false);

        foreach (var format in opts.Formats)
        {
            ct.ThrowIfCancellationRequested();
            await IngestFormatAsync(format, opts.LookbackDays, opts.MinParticipants, ct)
                .ConfigureAwait(false);
        }
    }

    public async Task IngestFormatAsync(
        string format,
        int lookbackDays,
        int minParticipants,
        CancellationToken ct)
    {
        var req = new TopDeckSearchRequest
        {
            Game = options.Value.Game,
            Format = format,
            LastDays = lookbackDays,
            ParticipantMin = minParticipants,
            Columns = ["decklist", "wins", "winsSwiss", "winsBracket",
                       "losses", "lossesSwiss", "lossesBracket", "draws"],
        };

        logger.LogInformation(
            "TopDeck: fetching {Format} tournaments, last {Days} days, ≥{Min} players",
            format, lookbackDays, minParticipants);

        var tournaments = await client.SearchTournamentsAsync(req, ct).ConfigureAwait(false);
        if (tournaments.Count == 0)
        {
            logger.LogInformation("TopDeck: {Format} returned no tournaments.", format);
            return;
        }

        // Batch writes — Fuseki's SPARQL Update endpoint 500s on multi-MB INSERT
        // DATA bodies. ~25 tournaments per flush keeps each request under a few
        // hundred KB; total throughput is comparable to one big request because
        // the bottleneck is parsing on the server side.
        const int batchSize = 25;
        var g = new RdfGraph();
        var entries = 0;
        var unresolved = 0;
        var done = 0;
        foreach (var t in tournaments)
        {
            ct.ThrowIfCancellationRequested();
            entries += TopDeckToRdfMapper.AssertTournament(g, t, scryfall, out var miss);
            unresolved += miss;
            done++;
            if (done % batchSize == 0)
            {
                await repo.WriteAsync(g, namedGraphUri: null, ct).ConfigureAwait(false);
                g = new RdfGraph();
            }
        }
        if (g.Triples.Count > 0)
            await repo.WriteAsync(g, namedGraphUri: null, ct).ConfigureAwait(false);

        logger.LogInformation(
            "TopDeck: wrote {Tournaments} {Format} tournaments, {Entries} entries; {Unresolved} card-name misses",
            tournaments.Count, format, entries, unresolved);
    }
}
