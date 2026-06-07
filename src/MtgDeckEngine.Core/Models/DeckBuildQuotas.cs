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
