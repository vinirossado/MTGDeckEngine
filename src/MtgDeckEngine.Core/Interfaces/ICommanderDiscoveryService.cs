using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Interfaces;

public sealed record CommanderDiscoveryFilter(
    /// <summary>Keep commanders whose decks fit at or below this bracket (1–5).</summary>
    int? MaxBracket = null,
    /// <summary>Keep commanders with at least one tournament deck at or under this price.</summary>
    decimal? MaxBudgetEur = null,
    /// <summary>
    /// Ignore commanders seen in fewer decks than this. Without a floor the
    /// list fills with one-off brews whose record is noise.
    /// </summary>
    int MinDeckCount = 3,
    int Limit = 25);

public interface ICommanderDiscoveryService
{
    /// <summary>
    /// The inverse of the deck builder: given a bracket and a budget, which
    /// commanders are worth building? Ranked by tournament win rate, shrunk
    /// toward the pool average so thin samples cannot top the list.
    /// </summary>
    Task<IReadOnlyList<CommanderPick>> FindCommandersAsync(
        CommanderDiscoveryFilter filter,
        CancellationToken cancellationToken = default);
}
