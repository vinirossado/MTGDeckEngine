using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core.Brackets;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Ingestion.Http;

namespace MtgDeckEngine.Ingestion;

/// <summary>
/// Bracket estimate backed by Commander Spellbook, degrading to the local
/// name-list evaluator when the service is unavailable or the commander cannot
/// be resolved. Spellbook is the only source here that sees two-card infinite
/// combos, which is what the local evaluator structurally misses.
/// </summary>
public sealed class SpellbookBracketService(
    CommanderSpellbookClient spellbook,
    ICommanderNameResolver names,
    ILogger<SpellbookBracketService> logger) : IBracketService
{
    private static readonly LocalBracketService Fallback = new();

    public async Task<DeckBracket> EvaluateAsync(
        string commanderSlug,
        IReadOnlyCollection<string> cardNames,
        CancellationToken cancellationToken = default)
    {
        var commanderName = await names.ResolveAsync(commanderSlug, cancellationToken)
            .ConfigureAwait(false);

        if (commanderName is not null)
        {
            var remote = await spellbook
                .EstimateBracketAsync(commanderName, cardNames, cancellationToken)
                .ConfigureAwait(false);
            if (remote is not null) return remote;
        }

        logger.LogInformation(
            "Falling back to local bracket estimate for {CommanderSlug}", commanderSlug);
        return await Fallback.EvaluateAsync(commanderSlug, cardNames, cancellationToken)
            .ConfigureAwait(false);
    }

}
