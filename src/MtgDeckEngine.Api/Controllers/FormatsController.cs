using Microsoft.AspNetCore.Mvc;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Api.Controllers;

[ApiController]
[Route("api/formats")]
public sealed class FormatsController(IFormatService svc) : ControllerBase
{
    /// <summary>Every format we currently have tournament data for.</summary>
    [HttpGet("")]
    public async Task<ActionResult<IReadOnlyList<FormatSummary>>> List(CancellationToken ct = default)
        => Ok(await svc.ListFormatsAsync(ct));

    /// <summary>Aggregate signals for one format (tournament + deck + entry counts, top-cut counts).</summary>
    [HttpGet("{format}/meta")]
    public async Task<ActionResult<FormatMeta>> Meta(string format, CancellationToken ct = default)
        => Ok(await svc.GetFormatMetaAsync(format.ToUpperInvariant(), ct));

    /// <summary>
    /// Most-played cards in this format. <c>maxPlacement=4</c> restricts to top-4 decks.
    /// </summary>
    [HttpGet("{format}/staples")]
    public async Task<ActionResult<IReadOnlyList<FormatStaple>>> Staples(
        string format,
        [FromQuery] decimal? maxPriceEur,
        [FromQuery] int? maxPlacement,
        [FromQuery] int minDeckCount = 2,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
        => Ok(await svc.GetFormatStaplesAsync(
            format.ToUpperInvariant(), maxPriceEur, maxPlacement, minDeckCount, limit, ct));
}

[ApiController]
[Route("api/commanders")]
public sealed class CommanderListController(IFormatService svc) : ControllerBase
{
    /// <summary>List every commander we've ingested data for.</summary>
    [HttpGet("")]
    public async Task<ActionResult<IReadOnlyList<CommanderSummary>>> List(
        [FromQuery] int limit = 100, CancellationToken ct = default)
        => Ok(await svc.ListCommandersAsync(limit, ct));
}
