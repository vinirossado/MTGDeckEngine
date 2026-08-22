using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Ingestion.Http;

namespace MtgDeckEngine.Ingestion;

/// <summary>
/// Resolves commander slugs against the Scryfall bulk cache's slug index, which
/// is built from the same slug rules EDHREC uses.
/// </summary>
public sealed class ScryfallCommanderNameResolver(
    ScryfallBulkCache cards,
    ILogger<ScryfallCommanderNameResolver> logger) : ICommanderNameResolver
{
    public async Task<string?> ResolveAsync(
        string commanderSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            await cards.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return cards.TryGetBySlug(commanderSlug, out var card) ? card.Name : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not resolve commander name for {Slug}", commanderSlug);
            return null;
        }
    }
}
