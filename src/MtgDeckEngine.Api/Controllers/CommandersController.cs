using Microsoft.AspNetCore.Mvc;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Brackets;
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
    public async Task<IActionResult> Discover(
        [FromQuery] int? maxBracket,
        [FromQuery] decimal? maxBudgetEur,
        [FromQuery] int minDeckCount = 3,
        [FromQuery] int limit = 25,
        [FromQuery] bool explain = false,
        CancellationToken ct = default)
    {
        if (maxBracket is int b && b is < 1 or > 5)
            return BadRequest("maxBracket must be between 1 and 5");
        if (maxBudgetEur is <= 0)
            return BadRequest("maxBudgetEur must be > 0");

        var filter = new CommanderDiscoveryFilter(maxBracket, maxBudgetEur, minDeckCount, limit);
        if (explain) return Sparql(discovery.Explain(filter));

        var picks = await discovery.FindCommandersAsync(filter, ct);
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
    public async Task<IActionResult> Recommendations(
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
        [FromQuery] bool explain = false,
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
        if (explain)
            return Sparql(recs.ExplainRecommendations(slug, filter));

        var items = await recs.GetRecommendationsAsync(slug, filter, ct);
        return Ok(items);
    }

    /// <summary>
    /// Tournament-derived signals for the commander (entry count, top cuts, win
    /// rate, meta share). Sourced from EDHTop16 ingestion.
    /// </summary>
    [HttpGet("{slug}/meta")]
    public async Task<IActionResult> Meta(
        string slug, [FromQuery] bool explain = false, CancellationToken ct = default)
    {
        if (explain)
            return Sparql(recs.ExplainCommanderMeta(slug));

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
    ///
    /// <paramref name="themes"/> takes comma-separated theme keys — wheel,
    /// lifedrain, tokens, storm, stax, blink, sacrifice, counters, graveyard —
    /// and favours cards matching them. A preference on ranking, not a filter:
    /// a thin theme yields a normal deck rather than a broken one, and the
    /// response reports how many of the 99 actually matched.
    /// </summary>
    [HttpGet("{slug}/build-deck")]
    public async Task<IActionResult> BuildDeck(
        string slug,
        [FromQuery] decimal totalBudgetEur,
        [FromQuery] int? maxBracket,
        [FromQuery] decimal? maxCardPriceEur,
        [FromQuery] decimal? minSynergy,
        [FromQuery] bool excludeBasicLands = false,
        [FromQuery] string? excludeCategories = null,
        [FromQuery] string? themes = null,
        [FromQuery] bool explain = false,
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
        if (explain)
            return Sparql(recs.ExplainBuildDeck(slug, filter));

        var deck = await recs.BuildBudgetDeckAsync(
            slug, totalBudgetEur, filter, maxBracket, Split(themes), ct);
        return Ok(deck);
    }

    /// <summary>The themes a build can be asked to lean into.</summary>
    [HttpGet("themes")]
    public ActionResult<IEnumerable<object>> Themes()
        => Ok(DeckTheme.All.Select(t => new { t.Key, t.Name, t.Description }));

    /// <summary>
    /// A grid of buildable decks for the same commander and budget — one per
    /// bracket × strategy — so the trade-offs are side by side instead of
    /// collapsed into a single answer.
    /// </summary>
    /// <remarks>
    /// Defaults to brackets 2, 3 and 4 across all four strategies: twelve
    /// decks. Bracket 5 is absent on purpose — it is not derivable from a card
    /// list, so asking for one is asking for a Bracket 4 deck.
    ///
    /// Narrow it with comma-separated <c>brackets</c> and <c>strategies</c>
    /// (balanced, interactive, creatures, ramp). <c>includeCards=false</c>
    /// returns just the summaries, which is a great deal smaller.
    /// </remarks>
    [HttpGet("{slug}/build-deck/options")]
    public async Task<ActionResult<IReadOnlyList<DeckOption>>> BuildDeckOptions(
        string slug,
        [FromQuery] decimal totalBudgetEur,
        [FromQuery] string? brackets = null,
        [FromQuery] string? strategies = null,
        [FromQuery] decimal? maxCardPriceEur = null,
        [FromQuery] bool includeCards = true,
        CancellationToken ct = default)
    {
        if (totalBudgetEur <= 0)
            return BadRequest("totalBudgetEur must be > 0");

        var wanted = ParseBrackets(brackets);
        if (wanted is null)
            return BadRequest("brackets must be comma-separated numbers between 1 and 5");

        var filter = new RecommendationFilter(
            MaxPriceEur:       maxCardPriceEur,
            ExcludeBasicLands: false,
            Limit:             300);

        var options = await recs.BuildDeckOptionsAsync(
            slug, totalBudgetEur, filter, wanted, Split(strategies), ct);

        return Ok(includeCards
            ? options
            : options.Select(o => o with { Cards = [] }).ToList());
    }

    /// <summary>
    /// Null signals a malformed list; empty means "not specified, use defaults".
    /// </summary>
    private static IReadOnlyList<int>? ParseBrackets(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var b) || b is < 1 or > 5) return null;
            result.Add(b);
        }
        return result;
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

    /// <summary>
    /// Render queries as a runnable .sparql document rather than JSON, so it can
    /// go straight to a file:
    ///
    ///   curl '.../build-deck?totalBudgetEur=120&amp;explain=true' &gt; pool.sparql
    ///   bin/sparql pool.sparql
    ///
    /// Purposes are emitted as SPARQL comments, which are legal syntax.
    /// </summary>
    private ContentResult Sparql(IReadOnlyList<SparqlExplanation> queries)
        => Content(SparqlExplanation.ToDocument(queries), "text/plain; charset=utf-8");

    private static IReadOnlyList<string>? Split(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
