using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Interfaces;

public sealed record RecommendationFilter(
    decimal? MaxPriceEur = null,
    decimal? MinInclusionPct = null,
    decimal? MinSynergy = null,
    bool ExcludeLands = false,
    bool ExcludeBasicLands = true,
    IReadOnlyList<string>? ExcludeCategories = null,
    IReadOnlyList<string>? IncludeOnlyCategories = null,
    int Limit = 50);

public interface IDeckRecommendationService
{
    Task<IReadOnlyList<CardRecommendation>> GetRecommendationsAsync(
        string commanderSlug,
        RecommendationFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Greedy budget deck builder: picks the top-inclusion 99 cards (plus the
    /// commander) whose cumulative price ≤ <paramref name="totalBudgetEur"/>.
    /// Respects per-card price cap and the same exclusion filters as recommendations.
    /// </summary>
    Task<BudgetDeck> BuildBudgetDeckAsync(
        string commanderSlug,
        decimal totalBudgetEur,
        RecommendationFilter filter,
        CancellationToken ct);
}

public sealed record BudgetDeck(
    string CommanderSlug,
    decimal TotalPriceEur,
    int CardCount,
    IReadOnlyList<CardRecommendation> Cards);
