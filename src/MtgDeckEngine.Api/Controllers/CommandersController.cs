using Microsoft.AspNetCore.Mvc;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Api.Controllers;

[ApiController]
[Route("api/commanders")]
public sealed class CommandersController(IDeckRecommendationService recs) : ControllerBase
{
    /// <summary>
    /// Cards ranked by EDHREC inclusion + synergy for the given commander,
    /// filtered by price/category/type.
    /// </summary>
    /// <remarks>
    /// excludeCategories and includeOnlyCategories take comma-separated EDHREC
    /// category labels (e.g. "Lands,Mana Artifacts").
    /// </remarks>
    [HttpGet("{slug}/recommendations")]
    public async Task<ActionResult<IReadOnlyList<CardRecommendation>>> Recommendations(
        string slug,
        [FromQuery] decimal? maxPriceEur,
        [FromQuery] decimal? minInclusion,
        [FromQuery] decimal? minSynergy,
        [FromQuery] bool excludeLands = false,
        [FromQuery] bool excludeBasicLands = true,
        [FromQuery] string? excludeCategories = null,
        [FromQuery] string? includeOnlyCategories = null,
        [FromQuery] int limit = 50,
        [FromQuery] int? minTopCutAppearances = null,
        [FromQuery] int? maxPlacement = null,
        [FromQuery] RecommendationSource source = RecommendationSource.All,
        CancellationToken ct = default)
    {
        var filter = new RecommendationFilter(
            MaxPriceEur:            maxPriceEur,
            MinInclusionPct:        minInclusion,
            MinSynergy:             minSynergy,
            ExcludeLands:           excludeLands,
            ExcludeBasicLands:      excludeBasicLands,
            ExcludeCategories:      Split(excludeCategories),
            IncludeOnlyCategories:  Split(includeOnlyCategories),
            Limit:                  limit,
            MinTopCutAppearances:   minTopCutAppearances,
            MaxPlacement:           maxPlacement,
            Source:                 source);
        var items = await recs.GetRecommendationsAsync(slug, filter, ct);
        return Ok(items);
    }

    /// <summary>
    /// Tournament-derived signals for the commander (entry count, top cuts, win
    /// rate, meta share). Sourced from EDHTop16 ingestion.
    /// </summary>
    [HttpGet("{slug}/meta")]
    public async Task<ActionResult<CommanderMeta>> Meta(
        string slug, CancellationToken ct = default)
    {
        var meta = await recs.GetCommanderMetaAsync(slug, ct);
        return Ok(meta);
    }

    /// <summary>
    /// Greedy budget deck builder. Picks the top-inclusion cards (within filter)
    /// while cumulative price stays under <paramref name="totalBudgetEur"/>.
    /// </summary>
    [HttpGet("{slug}/build-deck")]
    public async Task<ActionResult<BudgetDeck>> BuildDeck(
        string slug,
        [FromQuery] decimal totalBudgetEur,
        [FromQuery] decimal? maxCardPriceEur,
        [FromQuery] decimal? minSynergy,
        [FromQuery] bool excludeBasicLands = false,
        [FromQuery] string? excludeCategories = null,
        CancellationToken ct = default)
    {
        if (totalBudgetEur <= 0)
            return BadRequest("totalBudgetEur must be > 0");

        var filter = new RecommendationFilter(
            MaxPriceEur:       maxCardPriceEur,
            MinInclusionPct:   null,
            MinSynergy:        minSynergy,
            ExcludeLands:      false,
            ExcludeBasicLands: excludeBasicLands,
            ExcludeCategories: Split(excludeCategories),
            Limit:             300);
        var deck = await recs.BuildBudgetDeckAsync(slug, totalBudgetEur, filter, ct);
        return Ok(deck);
    }

    private static IReadOnlyList<string>? Split(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
