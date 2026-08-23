namespace MtgDeckEngine.Core.Models;

/// <summary>
/// One buildable deck in a grid of bracket × strategy options, so a commander
/// and a budget produce a set of real alternatives rather than a single answer.
/// </summary>
public sealed record DeckOption(
    /// <summary>Bracket the finished list actually grades at.</summary>
    int Bracket,
    string BracketLabel,
    /// <summary>
    /// The cap this option was built under. It can exceed <see cref="Bracket"/>
    /// — asking for a Bracket 4 build does not force the result to reach 4 if
    /// the budget or the card pool will not support it.
    /// </summary>
    int RequestedBracket,
    string StrategyKey,
    string StrategyName,
    string StrategyDescription,
    decimal TotalPriceEur,
    int CardCount,
    /// <summary>
    /// Mean card score, 0–1. Comparable <b>within</b> a bracket; across
    /// brackets it is not, because a higher bracket may use cards a lower one
    /// is forbidden from touching.
    /// </summary>
    decimal Score,
    string? CommanderName,
    IReadOnlyList<CardRecommendation> Cards,
    DeckBracket? BracketDetail);
