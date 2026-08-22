using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Interfaces;

public interface ISavedDeckService
{
    Task<SavedDeck> SaveAsync(
        string name,
        string commanderSlug,
        IReadOnlyList<CardRecommendation> cards,
        DeckBracket? bracket,
        string? notes,
        decimal? budgetEur,
        string? commanderName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedDeckSummary>> ListAsync(
        string? commanderSlug = null,
        CancellationToken cancellationToken = default);

    Task<SavedDeck?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
