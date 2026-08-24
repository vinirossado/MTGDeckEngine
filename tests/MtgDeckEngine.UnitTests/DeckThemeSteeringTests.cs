using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Brackets;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Mappers;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class DeckThemeSteeringTests
{
    private const string Slug = "test-commander";
    private static readonly string[] Identity = { "U", "R" };

    private const string WheelText = "Each player discards their hand, then draws seven cards.";
    private const string PlainText = "Flying.";

    /// <summary>
    /// Wheels are deliberately the *worst* cards in the pool by inclusion. If a
    /// themed build still picks them, the theme is steering; if it picks the
    /// high-inclusion filler instead, it is decorative.
    /// </summary>
    private static async Task<DeckRecommendationService> SeedAsync()
    {
        var repo = new InMemoryGraphRepository();
        var global = new RdfGraph();
        var entries = new List<EdhrecCardEntry>();
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string oid, string name, string type, string text, decimal inclusion)
        {
            ScryfallToRdfMapper.AssertCard(global, new CardDto(
                oid, name, Identity, Identity, type, text, 0.20m, null, true));
            entries.Add(new EdhrecCardEntry(name, Slug, "creatures", inclusion, 1m, 100, 1000)
            {
                CategoryLabel = "Top Cards",
            });
            nameToId[name] = oid;
        }

        for (var i = 0; i < 90; i++)
            Add($"fill-{i:D2}", $"Filler {i:D2}", "Enchantment", PlainText, inclusion: 90m);
        for (var i = 0; i < 15; i++)
            Add($"wheel-{i:D2}", $"Wheel {i:D2}", "Sorcery", WheelText, inclusion: 2m);
        for (var i = 0; i < 45; i++)
            Add($"land-{i:D2}", $"Land {i:D2}", "Land", "", inclusion: 50m);

        await repo.WriteAsync(global, null, default);
        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(Slug)), default);
        return new DeckRecommendationService(repo);
    }

    private static RecommendationFilter Filter() => new(ExcludeBasicLands: false, Limit: 300);

    private static int WheelsIn(BudgetDeck d)
        => d.Cards.Count(c => c.Name.StartsWith("Wheel ", StringComparison.Ordinal));

    [Fact]
    public async Task Asking_for_a_theme_puts_more_of_it_in_the_deck()
    {
        var svc = await SeedAsync();

        var plain  = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), null, default);
        var themed = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), null, ["wheel"], default);

        Assert.True(WheelsIn(themed) > WheelsIn(plain),
            $"themed had {WheelsIn(themed)} wheels, plain had {WheelsIn(plain)}");
    }

    [Fact]
    public async Task The_deck_is_still_complete_and_within_budget()
    {
        // A theme is a preference, not a filter — it must not starve the deck.
        var svc = await SeedAsync();

        var themed = await svc.BuildBudgetDeckAsync(Slug, 30m, Filter(), null, ["wheel"], default);

        Assert.Equal(99, themed.CardCount);
        Assert.True(themed.TotalPriceEur <= 30m);
    }

    [Fact]
    public async Task Reports_matches_against_what_was_available()
    {
        // The match count alone is uninterpretable: 7 of 10 is the theme
        // working, 7 of 60 is it barely trying.
        var svc = await SeedAsync();

        var themed = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), null, ["wheel"], default);

        Assert.NotNull(themed.ThemeMatchCount);
        Assert.NotNull(themed.ThemeCandidateCount);
        Assert.Equal(15, themed.ThemeCandidateCount);
        Assert.InRange(themed.ThemeMatchCount!.Value, 1, themed.ThemeCandidateCount!.Value);
        Assert.Equal(["wheel"], themed.Themes!);
    }

    [Fact]
    public async Task An_unthemed_build_reports_no_theme_figures()
    {
        var svc = await SeedAsync();

        var plain = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), null, default);

        Assert.Null(plain.ThemeMatchCount);
        Assert.Null(plain.ThemeCandidateCount);
    }

    [Fact]
    public async Task A_theme_the_pool_cannot_serve_still_builds()
    {
        // Nothing here drains life. The request must degrade to a normal deck
        // rather than fail or come up short.
        var svc = await SeedAsync();

        var themed = await svc.BuildBudgetDeckAsync(Slug, 100m, Filter(), null, ["lifedrain"], default);

        Assert.Equal(99, themed.CardCount);
        Assert.Equal(0, themed.ThemeCandidateCount);
        Assert.Equal(0, themed.ThemeMatchCount);
    }
}
