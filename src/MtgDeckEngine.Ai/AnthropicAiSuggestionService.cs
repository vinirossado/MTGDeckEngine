using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Ai;

/// <summary>
/// Calls Anthropic's Messages API directly via HttpClient — no SDK dependency.
/// Uses prompt caching (cache_control on the graph-context block) so repeated
/// calls for the same commander only pay the system-prompt and short user
/// message in fresh input tokens.
/// </summary>
public sealed class AnthropicAiSuggestionService(
    HttpClient http,
    IDeckRecommendationService recs,
    IOptions<AiOptions> options,
    ILogger<AnthropicAiSuggestionService> logger) : IAiSuggestionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AiSuggestionResponse> SuggestAsync(AiSuggestionRequest req, CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.AnthropicApiKey))
            throw new InvalidOperationException(
                "Ai.AnthropicApiKey not set. Configure via `dotnet user-secrets set \"Ai:AnthropicApiKey\" \"sk-ant-...\"`.");

        // Gather graph context: top tournament-played + high-synergy cards for
        // this commander. Build a single text block — cacheable across users.
        var context = await recs.GetRecommendationsAsync(
            req.CommanderSlug,
            new RecommendationFilter(
                Source: RecommendationSource.All,
                MaxPriceEur: req.MaxPriceEur,
                MaxPlacement: req.MaxPlacement,
                ExcludeBasicLands: true,
                Limit: opts.ContextCardCount),
            ct).ConfigureAwait(false);

        var contextBlock = BuildContextBlock(req.CommanderSlug, context);
        var currentDeckBlock = BuildCurrentDeckBlock(req.CurrentDeckCardNames);

        var messages = new AnthropicMessageRequest
        {
            Model = opts.Model,
            MaxTokens = opts.MaxOutputTokens,
            System = new()
            {
                new SystemBlock
                {
                    Type = "text",
                    Text = SystemPrompt,
                },
                new SystemBlock
                {
                    Type = "text",
                    Text = contextBlock,
                    CacheControl = new CacheControl { Type = "ephemeral" },
                },
            },
            Messages = new()
            {
                new() { Role = "user", Content = currentDeckBlock },
            },
        };

        var resp = await http.PostAsJsonAsync(
            "https://api.anthropic.com/v1/messages", messages, Json, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            logger.LogError("Anthropic HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }
        var body2 = await resp.Content.ReadFromJsonAsync<AnthropicMessageResponse>(Json, ct).ConfigureAwait(false);
        if (body2 is null) throw new InvalidOperationException("Anthropic returned empty body.");

        var text = string.Concat(body2.Content.Select(c => c.Text));
        var suggestions = ParseSuggestions(text, context);

        return new AiSuggestionResponse(
            CommanderSlug: req.CommanderSlug,
            Reasoning:     text,
            Suggestions:   suggestions,
            Usage:         new AiUsage(
                InputTokens:           body2.Usage?.InputTokens ?? 0,
                OutputTokens:          body2.Usage?.OutputTokens ?? 0,
                CacheReadTokens:       body2.Usage?.CacheReadInputTokens ?? 0,
                CacheCreationTokens:   body2.Usage?.CacheCreationInputTokens ?? 0));
    }

    private const string SystemPrompt = """
        You are a Magic: The Gathering deck-building advisor. You will receive:
        1. A block of context describing the most-played and highest-synergy cards
           for a particular Commander, sourced from a knowledge graph that fuses
           EDHREC popularity, EDHTop16 / TopDeck.gg tournament results, and
           Scryfall card data.
        2. The user's current deck list (free-form card names).

        Return a short numbered list of up to 5 suggested *additions* or *swaps*,
        each one phrased as:

          <Card Name>  —  one-sentence reason, citing inclusion %, synergy score
                          or tournament appearances when known.

        Only recommend cards that appear in the context block. If the user's
        list is incomplete, focus on the highest-leverage missing staples.
        Do not invent prices or stats.
        """;

    private static string BuildContextBlock(string commanderSlug, IReadOnlyList<CardRecommendation> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Commander knowledge-graph context — {commanderSlug}");
        sb.AppendLine();
        sb.AppendLine("Card | Category | Inclusion% | Synergy | TopCutDecks | Price€");
        sb.AppendLine("--- | --- | --- | --- | --- | ---");
        foreach (var c in cards)
        {
            sb.Append(c.Name).Append(" | ")
              .Append(c.Category ?? "-").Append(" | ")
              .Append(c.InclusionPct?.ToString("0.0") ?? "-").Append(" | ")
              .Append(c.SynergyScore?.ToString("0.00") ?? "-").Append(" | ")
              .Append(c.TopCutAppearances?.ToString() ?? "-").Append(" | ")
              .Append(c.PriceEur?.ToString("0.00") ?? "-").AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildCurrentDeckBlock(IReadOnlyList<string> cardNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("My current deck contains:");
        if (cardNames.Count == 0) sb.AppendLine("(no cards listed — suggest from scratch)");
        foreach (var n in cardNames) sb.Append("- ").AppendLine(n);
        sb.AppendLine();
        sb.AppendLine("Please suggest the most impactful additions or swaps.");
        return sb.ToString();
    }

    private static IReadOnlyList<AiCardSuggestion> ParseSuggestions(
        string text, IReadOnlyList<CardRecommendation> context)
    {
        // The model's primary output is prose. We do a best-effort match:
        // any card name in the context that appears in the model's text
        // becomes a structured suggestion. The whole prose lives in Reasoning.
        var byName = context.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var found = new List<AiCardSuggestion>();
        foreach (var card in context)
        {
            if (text.Contains(card.Name, StringComparison.OrdinalIgnoreCase))
                found.Add(new AiCardSuggestion(card.Name, "", card.PriceEur));
        }
        return found;
    }
}

// -------- Anthropic API DTOs --------

internal sealed class AnthropicMessageRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
    [JsonPropertyName("system")] public List<SystemBlock> System { get; set; } = new();
    [JsonPropertyName("messages")] public List<Message> Messages { get; set; } = new();
}

internal sealed class SystemBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("cache_control")] public CacheControl? CacheControl { get; set; }
}

internal sealed class CacheControl
{
    [JsonPropertyName("type")] public string Type { get; set; } = "ephemeral";
}

internal sealed class Message
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

internal sealed class AnthropicMessageResponse
{
    [JsonPropertyName("content")] public List<ContentBlock> Content { get; set; } = new();
    [JsonPropertyName("usage")] public Usage? Usage { get; set; }
}

internal sealed class ContentBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

internal sealed class Usage
{
    [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
    [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    [JsonPropertyName("cache_read_input_tokens")] public int? CacheReadInputTokens { get; set; }
    [JsonPropertyName("cache_creation_input_tokens")] public int? CacheCreationInputTokens { get; set; }
}
