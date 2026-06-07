using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Ai;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Api.Controllers;

[ApiController]
[Route("api/commanders")]
public sealed class AiSuggestController(
    IAiSuggestionService ai,
    IOptions<AiOptions> aiOpts) : ControllerBase
{
    /// <summary>
    /// Ask Claude for additions/swaps to a current deck list, given the
    /// commander's tournament + EDHREC context from the knowledge graph.
    /// </summary>
    /// <remarks>
    /// Disabled by default — enable with
    /// <c>dotnet user-secrets set "Ai:Enabled" true</c> and
    /// <c>dotnet user-secrets set "Ai:AnthropicApiKey" "sk-ant-..."</c>.
    /// </remarks>
    [HttpPost("{slug}/ai/suggest")]
    public async Task<ActionResult<AiSuggestionResponse>> Suggest(
        string slug,
        [FromBody] AiSuggestBody body,
        CancellationToken ct = default)
    {
        if (!aiOpts.Value.Enabled)
            return StatusCode(503, new { error = "AI suggestions are disabled. Set Ai:Enabled=true." });
        if (string.IsNullOrWhiteSpace(aiOpts.Value.AnthropicApiKey))
            return StatusCode(503, new { error = "Ai:AnthropicApiKey is not configured." });

        var request = new AiSuggestionRequest(
            CommanderSlug:        slug,
            CurrentDeckCardNames: body.CurrentDeckCardNames ?? Array.Empty<string>(),
            MaxPriceEur:          body.MaxPriceEur,
            MaxPlacement:         body.MaxPlacement);
        var response = await ai.SuggestAsync(request, ct);
        return Ok(response);
    }
}

public sealed class AiSuggestBody
{
    public IReadOnlyList<string>? CurrentDeckCardNames { get; set; }
    public decimal? MaxPriceEur { get; set; }
    public int? MaxPlacement { get; set; }
}
