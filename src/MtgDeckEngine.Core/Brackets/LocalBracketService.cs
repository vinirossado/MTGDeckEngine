using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Brackets;

/// <summary>
/// Offline bracket estimate from curated name lists only. No network, no combo
/// detection — used as the fallback when Commander Spellbook is unreachable and
/// as the default in unit tests.
/// </summary>
public sealed class LocalBracketService : IBracketService
{
    public Task<DeckBracket> EvaluateAsync(
        string commanderSlug,
        IReadOnlyCollection<string> cardNames,
        CancellationToken cancellationToken = default)
        => Task.FromResult(BracketEvaluator.Evaluate(cardNames));
}
