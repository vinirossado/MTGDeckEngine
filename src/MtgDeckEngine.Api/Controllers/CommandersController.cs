using Microsoft.AspNetCore.Mvc;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Api.Controllers;

[ApiController]
[Route("api/commanders")]
public sealed class CommandersController(
    IDeckRecommendationService recs,
    ICommanderNameResolver commanderNames,
    ICommanderDiscoveryService discovery) : ControllerBase
{
    /// <summary>
    /// The inverse of the deck builder: instead of "build a deck for this
    /// commander", answers "which commanders are worth building at this bracket
    /// and this budget?".
    ///
    /// Ranked by tournament win rate shrunk toward the average of the filtered
    /// pool, so a commander with one lucky 3-0 does not outrank one with fifty
    /// events. <paramref name="maxBudgetEur"/> matches against the cheapest
    /// tournament deck actually recorded for that commander.
    /// </summary>
    [HttpGet("discover")]
    public async Task<ActionResult<IReadOnlyList<CommanderPick>>> Discover(
        [FromQuery] int? maxBracket,
        [FromQuery] decimal? maxBudgetEur,
        [FromQuery] int minDeckCount = 3,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        if (maxBracket is int b && b is < 1 or > 5)
            return BadRequest("maxBracket must be between 1 and 5");
        if (maxBudgetEur is <= 0)
            return BadRequest("maxBudgetEur must be > 0");

        var picks = await discovery.FindCommandersAsync(
            new CommanderDiscoveryFilter(maxBracket, maxBudgetEur, minDeckCount, limit), ct);
        return Ok(picks);
    }

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
    /// Budget deck builder. Returns a complete ~99-card deck (manabase completed
    /// with basic lands) that maximises a blended win-rate proxy while cumulative
    /// price stays within <paramref name="totalBudgetEur"/>, plus an estimated
    /// Commander Bracket. <paramref name="maxCardPriceEur"/> optionally caps the
    /// price of any single card. <paramref name="maxBracket"/> (1–5) constrains
    /// the build to stay at or below that Commander Bracket.
    /// </summary>
    [HttpGet("{slug}/build-deck")]
    public async Task<ActionResult<BudgetDeck>> BuildDeck(
        string slug,
        [FromQuery] decimal totalBudgetEur,
        [FromQuery] int? maxBracket,
        [FromQuery] decimal? maxCardPriceEur,
        [FromQuery] decimal? minSynergy,
        [FromQuery] bool excludeBasicLands = false,
        [FromQuery] string? excludeCategories = null,
        CancellationToken ct = default)
    {
        if (totalBudgetEur <= 0)
            return BadRequest("totalBudgetEur must be > 0");
        if (maxBracket is int b && b is < 1 or > 5)
            return BadRequest("maxBracket must be between 1 and 5");

        var filter = new RecommendationFilter(
            MaxPriceEur:       maxCardPriceEur,
            MinInclusionPct:   null,
            MinSynergy:        minSynergy,
            ExcludeLands:      false,
            ExcludeBasicLands: excludeBasicLands,
            ExcludeCategories: Split(excludeCategories),
            Limit:             300);
        var deck = await recs.BuildBudgetDeckAsync(slug, totalBudgetEur, filter, maxBracket, ct);
        return Ok(deck);
    }

    /// <summary>
    /// Build a deck and return it as plain "N Card Name" text rather than JSON —
    /// the same parameters as build-deck, piped straight into a decklist you can
    /// paste into Moxfield/Archidekt.
    /// </summary>
    [HttpGet("{slug}/build-deck/export")]
    [Produces("text/plain")]
    public async Task<IActionResult> BuildDeckExport(
        string slug,
        [FromQuery] decimal totalBudgetEur,
        [FromQuery] int? maxBracket,
        [FromQuery] decimal? maxCardPriceEur,
        CancellationToken ct = default)
    {
        if (totalBudgetEur <= 0)
            return BadRequest("totalBudgetEur must be > 0");
        if (maxBracket is int b && b is < 1 or > 5)
            return BadRequest("maxBracket must be between 1 and 5");

        var filter = new RecommendationFilter(
            MaxPriceEur:       maxCardPriceEur,
            ExcludeBasicLands: false,
            Limit:             300);
        var deck = await recs.BuildBudgetDeckAsync(slug, totalBudgetEur, filter, maxBracket, ct);
        var commanderName = deck.CommanderName ?? await commanderNames.ResolveAsync(slug, ct);
        return Content(DeckTextExporter.ToText(deck.Cards, commanderName), "text/plain; charset=utf-8");
    }

    private static IReadOnlyList<string>? Split(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
