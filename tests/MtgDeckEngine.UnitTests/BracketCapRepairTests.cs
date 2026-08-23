using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Mappers;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

/// <summary>
/// The builder cannot see two-card combos while building — they are a property
/// of card pairs, not of any card's flags. These cover the repair pass that
/// breaks them after the fact when a bracket cap demands it.
/// </summary>
public class BracketCapRepairTests
{
    private const string Slug = "test-commander";
    private static readonly string[] Identity = { "U", "R" };

    /// <summary>
    /// Stands in for Commander Spellbook. Reports cEDH while both halves of the
    /// combo are present and Core once either is gone, and counts its calls so
    /// the repair loop's cost is observable.
    /// </summary>
    private sealed class FakeBracketService(string comboA, string comboB) : IBracketService
    {
        public int Calls { get; private set; }

        public Task<DeckBracket> EvaluateAsync(
            string commanderSlug, IReadOnlyCollection<string> cardNames,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var hasCombo = cardNames.Contains(comboA) && cardNames.Contains(comboB);
            return Task.FromResult(hasCombo
                ? new DeckBracket(5, "cEDH", 0, [], false, false,
                    ["two-card infinite combo detected"], IsEstimate: false,
                    TwoCardCombos: [[comboA, comboB]])
                : new DeckBracket(2, "Core", 0, [], false, false,
                    ["no combos"], IsEstimate: false, TwoCardCombos: []));
        }
    }

    private static async Task<InMemoryGraphRepository> SeedAsync(params string[] extraNames)
    {
        var repo = new InMemoryGraphRepository();
        var global = new RdfGraph();
        var entries = new List<EdhrecCardEntry>();
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string oid, string name, decimal price, decimal inclusion)
        {
            ScryfallToRdfMapper.AssertCard(global, new CardDto(
                oid, name, Identity, Identity, "Creature — Spirit", null, price, null, true));
            entries.Add(new EdhrecCardEntry(name, Slug, "creatures", inclusion, 1m, 100, 1000)
            {
                CategoryLabel = "Creatures",
            });
            nameToId[name] = oid;
        }

        for (var i = 0; i < 120; i++) Add($"fill-{i:D3}", $"Filler {i:D3}", 0.10m, 1m);
        // The combo pieces score highest, so the upgrade pass will always reach
        // for them — which is what makes the ban list load-bearing.
        foreach (var (n, i) in extraNames.Select((n, i) => (n, i)))
            Add($"combo-{i}", n, 0.10m, 99m);

        await repo.WriteAsync(global, null, default);
        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(Slug)), default);
        return repo;
    }

    private static RecommendationFilter Filter() => new(ExcludeBasicLands: false, Limit: 300);

    [Fact]
    public async Task Breaks_a_combo_that_pushes_the_deck_past_the_cap()
    {
        var brackets = new FakeBracketService("Combo Piece A", "Combo Piece B");
        var svc = new DeckRecommendationService(
            await SeedAsync("Combo Piece A", "Combo Piece B"), brackets);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: 3, default);

        Assert.True(deck.Bracket!.Level <= 3, $"expected <= 3, got {deck.Bracket.Level}");
        var names = deck.Cards.Select(c => c.Name).ToList();
        Assert.False(names.Contains("Combo Piece A") && names.Contains("Combo Piece B"),
            "both combo halves survived the repair");
        Assert.Equal(99, deck.CardCount);
    }

    [Fact]
    public async Task Leaves_the_combo_alone_when_the_cap_allows_it()
    {
        // Brackets 4 and 5 permit combos, so there is nothing to repair and no
        // reason to spend a second evaluation on it.
        var brackets = new FakeBracketService("Combo Piece A", "Combo Piece B");
        var svc = new DeckRecommendationService(
            await SeedAsync("Combo Piece A", "Combo Piece B"), brackets);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: 4, default);

        Assert.Equal(5, deck.Bracket!.Level);
        Assert.Equal(1, brackets.Calls);
    }

    [Fact]
    public async Task An_uncapped_build_is_never_repaired()
    {
        var brackets = new FakeBracketService("Combo Piece A", "Combo Piece B");
        var svc = new DeckRecommendationService(
            await SeedAsync("Combo Piece A", "Combo Piece B"), brackets);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: null, default);

        Assert.Equal(5, deck.Bracket!.Level);
        Assert.Equal(1, brackets.Calls);
    }

    [Fact]
    public async Task Repair_converges_rather_than_looping()
    {
        var brackets = new FakeBracketService("Combo Piece A", "Combo Piece B");
        var svc = new DeckRecommendationService(
            await SeedAsync("Combo Piece A", "Combo Piece B"), brackets);

        await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: 2, default);

        // One evaluation, one repair, one confirmation. If the ban list were not
        // applied the upgrade pass would re-pick the piece it just cut and this
        // would run to the round limit.
        Assert.Equal(2, brackets.Calls);
    }

    [Fact]
    public async Task Gives_up_gracefully_when_the_combo_cannot_be_broken()
    {
        // A combo running through the commander cannot be repaired — the
        // commander is not one of the 99. Report the real bracket, do not spin.
        var brackets = new FakeBracketService("The Commander Itself", "Combo Piece B");
        var svc = new DeckRecommendationService(await SeedAsync("Combo Piece B"), brackets);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), maxBracket: 2, default);

        // "The Commander Itself" is never in the pool, so the fake never sees
        // both halves and reports Core — the point is that it terminates.
        Assert.NotNull(deck.Bracket);
        Assert.Equal(99, deck.CardCount);
        Assert.InRange(brackets.Calls, 1, 4);
    }
}
