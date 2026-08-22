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
    int Limit = 50,
    int? MinTopCutAppearances = null,
    int? MaxPlacement = null,
    RecommendationSource Source = RecommendationSource.All,
    // When false, cards need not have an EDHREC inclusion % to appear (the
    // EDHREC context block becomes OPTIONAL). The budget builder sets this
    // false so tournament-only staples and unpriced cards can still be drafted.
    bool RequireInclusion = true);

public enum RecommendationSource
{
    /// <summary>Score by EDHREC inclusion + synergy only (Phase 1 behaviour).</summary>
    Edhrec,
    /// <summary>Score by tournament top-cut appearances only.</summary>
    Tournament,
    /// <summary>Use both: must satisfy whichever filters are set; sort by tournament count then inclusion.</summary>
    All,
}

public interface IDeckRecommendationService
{
    Task<IReadOnlyList<CardRecommendation>> GetRecommendationsAsync(
        string commanderSlug,
        RecommendationFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Budget deck builder. Returns a complete ~99-card singleton deck (manabase
    /// completed with basic lands) whose cumulative price stays within
    /// <paramref name="totalBudgetEur"/>, maximising a blended win-rate proxy
    /// (tournament top-cut appearances + EDHREC inclusion/synergy) via a
    /// complete-then-upgrade greedy. Also attaches an estimated Commander Bracket.
    /// </summary>
    Task<BudgetDeck> BuildBudgetDeckAsync(
        string commanderSlug,
        decimal totalBudgetEur,
        RecommendationFilter filter,
        CancellationToken ct);

    /// <summary>
    /// As above, but additionally constrained to stay at or below
    /// <paramref name="maxBracket"/> (1–5). Cards the bracket forbids are kept
    /// out of the pool and the Game Changer allowance is enforced during the
    /// upgrade pass, so the build optimises within the bracket rather than
    /// being graded after the fact. Null means unconstrained.
    /// </summary>
    Task<BudgetDeck> BuildBudgetDeckAsync(
        string commanderSlug,
        decimal totalBudgetEur,
        RecommendationFilter filter,
        int? maxBracket,
        CancellationToken ct);

    /// <summary>
    /// Tournament-derived commander meta: entry counts, top-cut counts, win rate.
    /// Combines EDHTop16 aggregate stats with derived counts from individual
    /// tournament entries we've ingested.
    /// </summary>
    Task<CommanderMeta> GetCommanderMetaAsync(
        string commanderSlug,
        CancellationToken ct);
}

public sealed record BudgetDeck(
    string CommanderSlug,
    decimal TotalPriceEur,
    int CardCount,
    IReadOnlyList<CardRecommendation> Cards,
    DeckBracket? Bracket = null,
    // Printed card name for the slug. Carried on the response because callers
    // cannot derive it: un-slugifying is lossy, and the commander list endpoint
    // is capped, so a low-play commander simply is not in it.
    string? CommanderName = null);
