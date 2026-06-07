namespace MtgDeckEngine.Core.Models;

public sealed record AiSuggestionRequest(
    string CommanderSlug,
    IReadOnlyList<string> CurrentDeckCardNames,
    decimal? MaxPriceEur = null,
    int? MaxPlacement = null);

public sealed record AiSuggestionResponse(
    string CommanderSlug,
    string Reasoning,
    IReadOnlyList<AiCardSuggestion> Suggestions,
    AiUsage Usage);

public sealed record AiCardSuggestion(
    string Name,
    string Why,
    decimal? PriceEur = null);

public sealed record AiUsage(
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheCreationTokens);
