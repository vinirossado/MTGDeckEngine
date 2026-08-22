namespace MtgDeckEngine.Core.Models;

/// <summary>A deck the user built and kept for later review.</summary>
public sealed record SavedDeck(
    string Id,
    string Name,
    string CommanderSlug,
    decimal TotalPriceEur,
    int CardCount,
    DateTimeOffset SavedAt,
    IReadOnlyList<CardRecommendation> Cards,
    DeckBracket? Bracket = null,
    string? Notes = null,
    decimal? BudgetEur = null,
    string? CommanderName = null);

/// <summary>Row shape for the deck list — no card payload, so listing stays cheap.</summary>
public sealed record SavedDeckSummary(
    string Id,
    string Name,
    string CommanderSlug,
    decimal TotalPriceEur,
    int CardCount,
    DateTimeOffset SavedAt,
    int? BracketLevel,
    string? BracketLabel,
    decimal? BudgetEur);

public sealed record SaveDeckRequest(
    string CommanderSlug,
    string? Name = null,
    string? Notes = null,
    decimal? BudgetEur = null,
    IReadOnlyList<SaveDeckCard>? Cards = null);

public sealed record SaveDeckCard(string OracleId, string Name);
