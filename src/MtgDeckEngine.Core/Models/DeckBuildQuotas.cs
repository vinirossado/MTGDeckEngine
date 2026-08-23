namespace MtgDeckEngine.Core.Models;

/// <summary>
/// Per-archetype card targets for a Commander deck. Values are soft targets
/// — the builder fills up to <see cref="Target"/> from each category, then
/// uses the rest of the budget for whatever scores highest regardless of category.
/// </summary>
public sealed record DeckBuildQuotas(
    int Lands = 37,
    int Ramp = 10,
    int Draw = 10,
    int Removal = 8,
    int Creatures = 20,
    int Other = 14)
{
    public static DeckBuildQuotas Default { get; } = new();

    public int Sum => Lands + Ramp + Draw + Removal + Creatures + Other;
}

/// <summary>
/// A named way to spend the 99 slots. Changing the quotas changes which cards
/// clear the skeleton, so two strategies over the same pool and budget really
/// do produce different decks rather than the same list reshuffled.
///
/// These splits are a heuristic, not something the data told us. Deriving them
/// from the category mix of winning tournament decks per commander would be
/// better and is not what this does.
/// </summary>
public sealed record DeckStrategy(
    string Key,
    string Name,
    string Description,
    DeckBuildQuotas Quotas)
{
    public static readonly DeckStrategy Balanced = new(
        "balanced", "Balanced",
        "The default split: enough ramp and draw to function, a normal amount of interaction.",
        new DeckBuildQuotas(Lands: 37, Ramp: 10, Draw: 10, Removal: 8, Creatures: 20, Other: 14));

    public static readonly DeckStrategy Interactive = new(
        "interactive", "Interactive",
        "More removal and card draw, fewer creatures. Answers other decks rather than racing them.",
        new DeckBuildQuotas(Lands: 37, Ramp: 9, Draw: 13, Removal: 15, Creatures: 14, Other: 11));

    public static readonly DeckStrategy Creatures = new(
        "creatures", "Creature-heavy",
        "A wide creature base with light interaction. Applies pressure and plays to the board.",
        new DeckBuildQuotas(Lands: 36, Ramp: 9, Draw: 8, Removal: 5, Creatures: 30, Other: 11));

    public static readonly DeckStrategy Ramp = new(
        "ramp", "Ramp-forward",
        "Extra acceleration and fixing to deploy expensive payoffs ahead of schedule.",
        new DeckBuildQuotas(Lands: 38, Ramp: 17, Draw: 10, Removal: 7, Creatures: 17, Other: 10));

    public static readonly IReadOnlyList<DeckStrategy> All =
        [Balanced, Interactive, Creatures, Ramp];
}
