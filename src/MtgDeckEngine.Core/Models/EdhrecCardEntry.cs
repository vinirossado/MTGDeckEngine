namespace MtgDeckEngine.Core.Models;

public sealed record EdhrecCardEntry(
    string Name,
    string CommanderSlug,
    string Category,
    decimal InclusionPct,
    decimal SynergyScore,
    int NumDecks,
    int PotentialDecks)
{
    /// <summary>Human-readable name of the EDHREC category (e.g. "Card Draw").</summary>
    public string? CategoryLabel { get; init; }
}
