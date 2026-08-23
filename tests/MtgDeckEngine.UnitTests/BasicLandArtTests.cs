using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ingestion.Mappers;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class BasicLandArtTests
{
    private const string Slug = "test-commander";
    private static readonly string[] Identity = { "U" };

    private static async Task<DeckRecommendationService> SeedAsync(bool includeBasics)
    {
        var repo = new InMemoryGraphRepository();
        var global = new RdfGraph();
        var entries = new List<EdhrecCardEntry>();
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddCard(string oid, string name, string typeLine, decimal? price, string? img)
            => ScryfallToRdfMapper.AssertCard(global, new CardDto(
                oid, name, Identity, Identity, typeLine, null, price, null, true, img));

        for (var i = 0; i < 90; i++)
        {
            AddCard($"fill-{i:D2}", $"Filler {i:D2}", "Creature — Spirit", 0.10m, null);
            entries.Add(new EdhrecCardEntry($"Filler {i:D2}", Slug, "creatures", 1m, 1m, 100, 1000)
            {
                CategoryLabel = "Creatures",
            });
            nameToId[$"Filler {i:D2}"] = $"fill-{i:D2}";
        }

        if (includeBasics)
            AddCard("b2c6aa39-real-oracle-id", "Island", "Basic Land — Island", 0.05m,
                "https://cards.scryfall.io/normal/island.jpg");

        await repo.WriteAsync(global, null, default);
        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(Slug)), default);
        return new DeckRecommendationService(repo);
    }

    private static RecommendationFilter Filter() => new(ExcludeBasicLands: false, Limit: 300);

    [Fact]
    public async Task Basics_carry_the_real_card_art_and_oracle_id()
    {
        // A synthesised id like "basic-island-0" left every basic as a blank
        // tile: no art on the card, and nothing a client could resolve art from
        // either, since that lookup needs a real oracle id.
        var svc = await SeedAsync(includeBasics: true);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 20m, Filter(), null, default);
        var basics = deck.Cards.Where(c => c.Name == "Island").ToList();

        Assert.NotEmpty(basics);
        Assert.All(basics, b =>
        {
            Assert.Equal("b2c6aa39-real-oracle-id", b.OracleId);
            Assert.Equal("https://cards.scryfall.io/normal/island.jpg", b.ImageUrl);
        });
    }

    [Fact]
    public async Task Basics_stay_free_even_though_the_card_has_a_price()
    {
        // Scryfall prices an Island at a few cents. Charging the budget for 37
        // of them would distort every build, so they are always zero.
        var svc = await SeedAsync(includeBasics: true);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 20m, Filter(), null, default);

        Assert.All(deck.Cards.Where(c => c.Name == "Island"),
            b => Assert.Equal(0m, b.PriceEur));
    }

    [Fact]
    public async Task Repeated_basics_are_separate_copies_not_one()
    {
        // They now share an oracle id, so nothing downstream may collapse them.
        var svc = await SeedAsync(includeBasics: true);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 20m, Filter(), null, default);

        Assert.Equal(99, deck.CardCount);
        Assert.Equal(99, deck.Cards.Count);
        Assert.True(deck.Cards.Count(c => c.Name == "Island") > 1,
            "expected several copies of the basic, not one");
    }

    [Fact]
    public async Task A_missing_basic_still_fills_the_manabase()
    {
        // If the card is absent from the graph the deck must still reach 99
        // rather than coming up short — a blank tile beats a short manabase.
        var svc = await SeedAsync(includeBasics: false);

        var deck = await svc.BuildBudgetDeckAsync(Slug, 20m, Filter(), null, default);

        Assert.Equal(99, deck.CardCount);
        Assert.Contains(deck.Cards, c => c.Name == "Island");
    }
}
