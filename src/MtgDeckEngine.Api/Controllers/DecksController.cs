using Microsoft.AspNetCore.Mvc;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Api.Controllers;

/// <summary>
/// Decks the user built and kept. Stored as RDF in a dedicated named graph, so
/// they survive restarts and are queryable alongside the rest of the graph.
/// </summary>
[ApiController]
[Route("api/decks")]
public sealed class DecksController(
    ISavedDeckService decks,
    IDeckRecommendationService recs,
    ICommanderNameResolver commanderNames) : ControllerBase
{
    /// <summary>All saved decks, newest first. Optionally filtered by commander.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedDeckSummary>>> List(
        [FromQuery] string? commander = null,
        CancellationToken ct = default)
        => Ok(await decks.ListAsync(commander, ct));

    /// <summary>One saved deck, with its full card list.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SavedDeck>> Get(string id, CancellationToken ct = default)
        => await decks.GetAsync(id, ct) is { } deck ? Ok(deck) : NotFound();

    /// <summary>
    /// The deck as plain "N Card Name" text, with the commander in a trailing
    /// block — the format Moxfield, Archidekt, MTGGoldfish and MTGO import.
    /// Served as text/plain so `curl ... > deck.txt` just works.
    /// </summary>
    [HttpGet("{id}/export")]
    [Produces("text/plain")]
    public async Task<IActionResult> Export(string id, CancellationToken ct = default)
    {
        var deck = await decks.GetAsync(id, ct);
        if (deck is null) return NotFound();

        // Prefer the name captured at save time; fall back to resolving the slug
        // for decks saved before the name was persisted.
        var commanderName = deck.CommanderName
            ?? await commanderNames.ResolveAsync(deck.CommanderSlug, ct);
        var text = DeckTextExporter.ToText(deck.Cards, commanderName);
        return Content(text, "text/plain; charset=utf-8");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
        => await decks.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Build a deck for the given commander/budget/bracket and save it in one
    /// step. This is the path the UI uses — building and then saving separately
    /// would rebuild the deck and could return a different list, since the
    /// underlying prices and tournament data move between calls.
    /// </summary>
    [HttpPost("build-and-save")]
    public async Task<ActionResult<SavedDeck>> BuildAndSave(
        [FromBody] BuildAndSaveRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CommanderSlug))
            return BadRequest("commanderSlug is required");
        if (request.TotalBudgetEur <= 0)
            return BadRequest("totalBudgetEur must be > 0");
        if (request.MaxBracket is int b && b is < 1 or > 5)
            return BadRequest("maxBracket must be between 1 and 5");

        var filter = new RecommendationFilter(
            MaxPriceEur:       request.MaxCardPriceEur,
            MinInclusionPct:   null,
            MinSynergy:        request.MinSynergy,
            ExcludeLands:      false,
            ExcludeBasicLands: false,
            Limit:             300);

        var built = await recs.BuildBudgetDeckAsync(
            request.CommanderSlug, request.TotalBudgetEur, filter, request.MaxBracket, ct);

        var saved = await decks.SaveAsync(
            name:          request.Name ?? "",
            commanderSlug: request.CommanderSlug,
            cards:         built.Cards,
            bracket:       built.Bracket,
            notes:         request.Notes,
            budgetEur:     request.TotalBudgetEur,
            commanderName: built.CommanderName,
            cancellationToken: ct);

        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }
}

public sealed record BuildAndSaveRequest(
    string CommanderSlug,
    decimal TotalBudgetEur,
    int? MaxBracket = null,
    decimal? MaxCardPriceEur = null,
    decimal? MinSynergy = null,
    string? Name = null,
    string? Notes = null);
