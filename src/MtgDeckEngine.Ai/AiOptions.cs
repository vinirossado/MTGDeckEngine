namespace MtgDeckEngine.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Enable the /api/commanders/{slug}/ai/suggest endpoint.</summary>
    public bool Enabled { get; set; }

    /// <summary>Anthropic API key — store via `dotnet user-secrets`.</summary>
    public string AnthropicApiKey { get; set; } = "";

    /// <summary>Default Anthropic model. Update when newer model IDs ship.</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>Max output tokens the model is allowed to generate per call.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>How many ranked cards from the graph to inject as context.</summary>
    public int ContextCardCount { get; set; } = 80;
}
