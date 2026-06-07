using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Ontology;

namespace MtgDeckEngine.Ingestion.Workers;

/// <summary>
/// Runs once at startup: loads the ontology TBox + shapes into the triplestore,
/// then triggers EDHREC+Scryfall ingestion for each configured commander.
/// </summary>
public sealed class StartupIngestionWorker(
    IServiceProvider services,
    IGraphRepository repo,
    IOptions<IngestionOptions> opts,
    ILogger<StartupIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (opts.Value.DisableStartupRun)
        {
            logger.LogInformation("Startup ingestion disabled.");
            return;
        }

        try
        {
            logger.LogInformation("Loading TBox + SHACL shapes into the triplestore");
            await repo.LoadTurtleAsync(OntologyResources.Ontology, namedGraphUri: null, ct).ConfigureAwait(false);

            using var scope = services.CreateScope();
            var ingestor = scope.ServiceProvider.GetRequiredService<CommanderIngestor>();
            foreach (var slug in opts.Value.Commanders)
            {
                ct.ThrowIfCancellationRequested();
                await ingestor.IngestCommanderAsync(
                    slug,
                    opts.Value.MaxCardsPerCommander,
                    opts.Value.ScryfallDelayMs,
                    ct).ConfigureAwait(false);
            }
            logger.LogInformation("Startup ingestion complete.");
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup ingestion failed");
        }
    }
}
