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

public class DeckOptionsTests
{
    private const string Slug = "test-commander";
    private static readonly string[] Identity = { "U", "R", "G" };

    /// <summary>Grades on Game Changer count alone, per WotC's thresholds.</summary>
    private sealed class CountingBracketService : IBracketService
    {
        public Task<DeckBracket> EvaluateAsync(
            string commanderSlug, IReadOnlyCollection<string> cardNames,
            CancellationToken cancellationToken = default)
        {
            var gc = cardNames.Count(n => n.StartsWith("GameChanger", StringComparison.Ordinal));
            var (level, label) = BracketRules.Evaluate(
                new BracketRules.Signals(gc, false, false, 0, 0, false));
            return Task.FromResult(new DeckBracket(
                level, label, gc, [], false, false, [], IsEstimate: false, TwoCardCombos: []));
        }
    }

    /// <summary>Counts how often the card-pool query is issued.</summary>
    private sealed class CountingRepository(IGraphRepository inner) : IGraphRepository
    {
        private int _poolQueries;
        public int PoolQueries => _poolQueries;

        public Task<VDS.RDF.Query.SparqlResultSet> QueryAsync(string sparql, CancellationToken ct)
        {
            if (sparql.Contains("?topCutCount", StringComparison.Ordinal))
                Interlocked.Increment(ref _poolQueries);
            return inner.QueryAsync(sparql, ct);
        }

        public Task WriteAsync(VDS.RDF.IGraph g, Uri? u, CancellationToken ct) => inner.WriteAsync(g, u, ct);
        public Task LoadTurtleAsync(string t, Uri? u, CancellationToken ct) => inner.LoadTurtleAsync(t, u, ct);
        public Task DropGraphAsync(Uri u, CancellationToken ct) => inner.DropGraphAsync(u, ct);
        public Task UpdateAsync(string u, CancellationToken ct) => inner.UpdateAsync(u, ct);
    }

    /// <summary>
    /// Enough of every role that each strategy's quotas can be met, plus Game
    /// Changers that are actually flagged — without the flag the bracket
    /// constraint has nothing to bind on and every bracket builds the same deck.
    /// </summary>
    private static async Task SeedAsync(IGraphRepository repo)
    {
        var global = new RdfGraph();
        var entries = new List<EdhrecCardEntry>();
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string oid, string name, string type, string category,
                 decimal price, decimal inclusion, bool gameChanger = false)
        {
            ScryfallToRdfMapper.AssertCard(global, new CardDto(
                oid, name, Identity, Identity, type, null, price, null, true,
                ImageUrl: null, IsGameChanger: gameChanger));
            entries.Add(new EdhrecCardEntry(name, Slug, category.ToLowerInvariant(), inclusion, 1m, 100, 1000)
            {
                CategoryLabel = category,
            });
            nameToId[name] = oid;
        }

        for (var i = 0; i < 40; i++) Add($"cre-{i:D2}", $"Creature {i:D2}", "Creature — Spirit", "Creatures", 0.20m, 5m);
        for (var i = 0; i < 25; i++) Add($"ram-{i:D2}", $"Ramp {i:D2}", "Artifact", "Ramp", 0.20m, 5m);
        for (var i = 0; i < 25; i++) Add($"drw-{i:D2}", $"Draw {i:D2}", "Sorcery", "Card Draw", 0.20m, 5m);
        for (var i = 0; i < 25; i++) Add($"rem-{i:D2}", $"Removal {i:D2}", "Instant", "Removal", 0.20m, 5m);
        for (var i = 0; i < 25; i++) Add($"oth-{i:D2}", $"Other {i:D2}", "Enchantment", "Top Cards", 0.20m, 5m);
        for (var i = 0; i < 45; i++) Add($"lnd-{i:D2}", $"Land {i:D2}", "Land", "Lands", 0.20m, 5m);
        // Highest-scoring cards in the pool, so the upgrade pass reaches for
        // them and only the bracket cap holds them back.
        for (var i = 0; i < 8; i++)
            Add($"gc-{i}", $"GameChanger {i}", "Instant", "Top Cards", 1m, 99m, gameChanger: true);

        await repo.WriteAsync(global, null, default);
        var ctx = new RdfGraph();
        EdhrecToRdfMapper.AssertEntries(ctx, entries, nameToId);
        await repo.WriteAsync(ctx, new Uri(MtgVocab.CommanderContextUri(Slug)), default);
    }

    private static async Task<DeckRecommendationService> ServiceAsync()
    {
        var repo = new InMemoryGraphRepository();
        await SeedAsync(repo);
        return new DeckRecommendationService(repo, new CountingBracketService());
    }

    private static RecommendationFilter Filter() => new(ExcludeBasicLands: false, Limit: 300);

    private static int GameChangersIn(DeckOption o)
        => o.Cards.Count(c => c.Name.StartsWith("GameChanger", StringComparison.Ordinal));

    [Fact]
    public async Task Produces_an_option_per_bracket_and_strategy()
    {
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [2, 3], strategyKeys: null, default);

        Assert.Equal(8, options.Count);                       // 2 brackets x 4 strategies
        Assert.Equal(2, options.Select(o => o.RequestedBracket).Distinct().Count());
        Assert.Equal(4, options.Select(o => o.StrategyKey).Distinct().Count());
        Assert.All(options, o => Assert.Equal(99, o.CardCount));
    }

    [Fact]
    public async Task Honours_the_requested_brackets_and_strategies()
    {
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [3], strategyKeys: ["interactive", "ramp"], default);

        Assert.All(options, o => Assert.Equal(3, o.RequestedBracket));
        Assert.Equal(["interactive", "ramp"],
            options.Select(o => o.StrategyKey).Distinct().OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Strategies_produce_genuinely_different_decks()
    {
        // If the quotas did not bind, the four options would be one list under
        // four names and the feature would be decorative.
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [3], strategyKeys: null, default);

        var creatures = options.ToDictionary(
            o => o.StrategyKey,
            o => o.Cards.Count(c => (c.TypeLine ?? "").Contains("Creature")));

        Assert.True(creatures["creatures"] > creatures["interactive"],
            $"creature-heavy {creatures["creatures"]} vs interactive {creatures["interactive"]}");
    }

    [Fact]
    public async Task A_higher_bracket_may_spend_more_game_changers()
    {
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [2, 3], strategyKeys: ["balanced"], default);

        var b2 = options.Single(o => o.RequestedBracket == 2);
        var b3 = options.Single(o => o.RequestedBracket == 3);

        Assert.Equal(0, GameChangersIn(b2));
        Assert.InRange(GameChangersIn(b3), 1, 3);
    }

    [Fact]
    public async Task Identical_decks_from_different_caps_collapse_to_one()
    {
        // A cap that never binds gives the same list as a lower one. Reporting
        // it twice is noise, and the lower bracket is its honest label.
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [3, 4], strategyKeys: ["balanced"], default);

        var signatures = options
            .Select(o => string.Join('|', o.Cards.Select(c => c.OracleId).Order(StringComparer.Ordinal)))
            .ToList();
        Assert.Equal(signatures.Count, signatures.Distinct().Count());
    }

    [Fact]
    public async Task Never_offers_bracket_five()
    {
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: null, strategyKeys: null, default);

        Assert.NotEmpty(options);
        Assert.All(options, o => Assert.True(o.Bracket <= BracketRules.MaxDerivable));
        Assert.DoesNotContain(options, o => o.RequestedBracket == 5);
    }

    [Fact]
    public async Task Score_ignores_the_manabase()
    {
        // ~37 of the 99 are lands scoring near zero for every strategy. Folding
        // them into the mean drags all the options toward the same number and
        // hides the differences the grid exists to show.
        var svc = await ServiceAsync();

        var options = await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [3], strategyKeys: null, default);

        Assert.All(options, o => Assert.True(o.Score > 0m));
        Assert.True(options.Select(o => o.Score).Distinct().Count() > 1,
            "every strategy scored identically — the manabase is probably still in the mean");
    }

    [Fact]
    public async Task The_card_pool_is_fetched_once_for_the_whole_grid()
    {
        // Twelve builds each re-running the pool query would be the most
        // expensive thing this endpoint could do.
        var inner = new InMemoryGraphRepository();
        await SeedAsync(inner);
        var counting = new CountingRepository(inner);
        var svc = new DeckRecommendationService(counting, new CountingBracketService());

        await svc.BuildDeckOptionsAsync(
            Slug, 100m, Filter(), brackets: [2, 3, 4], strategyKeys: null, default);

        Assert.Equal(1, counting.PoolQueries);
    }
}
