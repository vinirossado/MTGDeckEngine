using Microsoft.AspNetCore.Mvc;
using MtgDeckEngine.Ingestion;

namespace MtgDeckEngine.Api.Controllers;

[ApiController]
[Route("api/commanders")]
public sealed class IngestController(IngestionOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// Synchronously ingest EDHREC + EDHTop16 (when enabled) for a single
    /// commander on demand. Use this when the frontend lets the user pick
    /// any commander, not just the ones in <c>IngestionOptions.Commanders</c>.
    /// </summary>
    /// <remarks>
    /// Expect 5–30 seconds per call: bulk Scryfall cache lookups are local,
    /// but EDHREC + EDHTop16 hit the network. The endpoint times out at
    /// 120s via the request cancellation token.
    /// </remarks>
    [HttpPost("{slug}/ingest")]
    public async Task<ActionResult<IngestSummary>> Ingest(
        string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { error = "Slug is required." });

        // Reject obviously bad slugs early.
        if (slug.Length > 120 || slug.Contains('/') || slug.Contains(' '))
            return BadRequest(new { error = "Slug must be kebab-case (e.g. 'atraxa-praetors-voice')." });

        var summary = await orchestrator.IngestCommanderAsync(slug, ct);
        return Ok(summary);
    }
}
