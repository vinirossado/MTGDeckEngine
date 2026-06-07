using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Interfaces;

public interface IAiSuggestionService
{
    /// <summary>
    /// Use the graph to gather top tournament-played and high-synergy cards
    /// for the commander, then ask Claude for natural-language suggestions
    /// against the user's current deck list.
    /// </summary>
    Task<AiSuggestionResponse> SuggestAsync(AiSuggestionRequest request, CancellationToken ct);
}
