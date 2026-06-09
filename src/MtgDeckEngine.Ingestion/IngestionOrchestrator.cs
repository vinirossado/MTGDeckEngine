using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Ingestion.Workers;

namespace MtgDeckEngine.Ingestion;

/// <summary>
/// Public façade for on-demand ingestion — used by the
/// <c>POST /api/commanders/{slug}/ingest</c> endpoint.
/// Runs CommanderIngestor (EDHREC + Scryfall) and, if enabled,
/// EdhTop16Ingestor for a single commander synchronously.
/// </summary>
public sealed class IngestionOrchestrator(
    CommanderIngestor commander,
    EdhTop16Ingestor edhtop,
    IOptions<IngestionOptions> opts,
    ILogger<IngestionOrchestrator> logger)
{
    public async Task<IngestSummary> IngestCommanderAsync(string slug, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var edhrecOk = false;
        var edhtopOk = false;
        string? error = null;

        try
        {
            logger.LogInformation("On-demand ingest starting for {Slug}", slug);
            await commander.IngestCommanderAsync(
                slug,
                opts.Value.MaxCardsPerCommander,
                opts.Value.ScryfallDelayMs,
                ct).ConfigureAwait(false);
            edhrecOk = true;

            if (opts.Value.EnableEdhTop16)
            {
                try
                {
                    await edhtop.IngestCommanderAsync(
                        slug,
                        opts.Value.MaxTournamentEntriesPerCommander,
                        ct).ConfigureAwait(false);
                    edhtopOk = true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "EDHTop16 ingestion failed for {Slug}; EDHREC data was written", slug);
                    error = $"EDHTop16: {ex.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "On-demand ingest failed for {Slug}", slug);
            error = ex.Message;
        }

        sw.Stop();
        logger.LogInformation(
            "On-demand ingest finished for {Slug} in {Ms} ms (edhrec={Edhrec}, edhtop16={Edhtop})",
            slug, sw.ElapsedMilliseconds, edhrecOk, edhtopOk);

        return new IngestSummary(
            CommanderSlug:   slug,
            EdhrecIngested:  edhrecOk,
            EdhTop16Ingested: edhtopOk,
            DurationMs:      sw.ElapsedMilliseconds,
            Error:           error);
    }
}

public sealed record IngestSummary(
    string CommanderSlug,
    bool EdhrecIngested,
    bool EdhTop16Ingested,
    long DurationMs,
    string? Error);
