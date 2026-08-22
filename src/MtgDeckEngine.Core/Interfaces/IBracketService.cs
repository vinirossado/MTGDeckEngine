using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Interfaces;

/// <summary>
/// Estimates a decklist's Commander Bracket. Implementations differ in how much
/// they can see: the local evaluator only knows curated name lists, while the
/// Commander Spellbook-backed one also detects two-card infinite combos.
/// </summary>
public interface IBracketService
{
    Task<DeckBracket> EvaluateAsync(
        string commanderSlug,
        IReadOnlyCollection<string> cardNames,
        CancellationToken cancellationToken = default);
}
