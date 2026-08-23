using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class SavedDeckServiceTests
{
    private const string Slug = "xyris-the-writhing-storm";

    private static SavedDeckService NewService() => new(new InMemoryGraphRepository());

    private static IReadOnlyList<CardRecommendation> Cards(int n = 3) =>
        Enumerable.Range(0, n)
            .Select(i => new CardRecommendation($"o{i}", $"Card {i}", "Creatures", null, null, 1.5m))
            .ToList();

    private static DeckBracket Bracket() => new(
        Level:             3,
        Label:             "Upgraded",
        GameChangerCount:  2,
        GameChangersFound: ["Rhystic Study", "Mystical Tutor"],
        HasMassLandDenial: false,
        HasExtraTurns:     true,
        Reasons:
        [
            "Commander Spellbook bracket tag 'P' -> Upgraded.",
            "2 Game Changer(s): Mystical Tutor, Rhystic Study.",
            "Contains extra-turn effects.",
        ],
        IsEstimate: false);

    [Fact]
    public async Task Bracket_reasoning_survives_a_save_and_reload()
    {
        // The whole point: reopening a saved deck must explain *why* it is
        // Bracket 3, not just assert the number.
        var svc = NewService();
        var saved = await svc.SaveAsync("My brew", Slug, Cards(), Bracket(), null, 100m);

        var loaded = await svc.GetAsync(saved.Id);

        Assert.NotNull(loaded);
        var b = Assert.IsType<DeckBracket>(loaded!.Bracket);
        Assert.Equal(3, b.Level);
        Assert.Equal("Upgraded", b.Label);
        Assert.False(b.IsEstimate);
        Assert.False(b.HasMassLandDenial);
        Assert.True(b.HasExtraTurns);
        Assert.Equal(2, b.GameChangerCount);
        Assert.Equal(["Mystical Tutor", "Rhystic Study"], b.GameChangersFound);
        Assert.Equal(Bracket().Reasons, b.Reasons);
    }

    [Fact]
    public async Task Reasons_come_back_in_the_order_they_were_written()
    {
        // RDF has no ordering, so the reasons are ordinal-tagged. Use text that
        // sorts differently from its intended order to catch an accidental
        // alphabetical read.
        var bracket = Bracket() with { Reasons = ["zebra first", "alpha second", "middle third"] };
        var svc = NewService();
        var saved = await svc.SaveAsync("ordered", Slug, Cards(), bracket, null, null);

        var loaded = await svc.GetAsync(saved.Id);

        Assert.Equal(["zebra first", "alpha second", "middle third"], loaded!.Bracket!.Reasons);
    }

    [Fact]
    public async Task Repeated_cards_round_trip_as_separate_copies()
    {
        // A basic-land manabase is many copies of one name; RDF has no multiset,
        // so each copy needs its own slot or they collapse into one.
        var cards = Enumerable.Repeat(
            new CardRecommendation("island", "Island", "Lands", null, null, 0m), 12).ToList();
        var svc = NewService();
        var saved = await svc.SaveAsync("basics", Slug, cards, null, null, null);

        var loaded = await svc.GetAsync(saved.Id);

        Assert.Equal(12, loaded!.Cards.Count);
        Assert.All(loaded.Cards, c => Assert.Equal("Island", c.Name));
    }

    [Fact]
    public async Task A_deck_saved_without_a_bracket_loads_without_one()
    {
        var svc = NewService();
        var saved = await svc.SaveAsync("no bracket", Slug, Cards(), null, null, null);

        var loaded = await svc.GetAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.Bracket);
    }

    [Fact]
    public async Task Deleting_a_deck_leaves_the_others_intact()
    {
        var svc = NewService();
        var keep = await svc.SaveAsync("keep", Slug, Cards(), Bracket(), null, null);
        var drop = await svc.SaveAsync("drop", Slug, Cards(), Bracket(), null, null);

        Assert.True(await svc.DeleteAsync(drop.Id));

        Assert.Null(await svc.GetAsync(drop.Id));
        var survivor = await svc.GetAsync(keep.Id);
        Assert.NotNull(survivor);
        // The surviving deck keeps its reasoning — the delete must not have
        // taken the shared-graph reason nodes with it.
        Assert.Equal(Bracket().Reasons, survivor!.Bracket!.Reasons);
    }

    [Fact]
    public async Task Deleting_a_full_size_deck_leaves_nothing_behind()
    {
        // Guards that a delete removes all three node kinds — the deck, its 99
        // slots, and its bracket-reason nodes — leaving the graph empty.
        //
        // NOTE: this does NOT reproduce the bug that motivated it. The delete
        // used to cross-multiply the deck's triples by every slot triple by
        // every reason triple; against Fuseki the resulting solution set was
        // truncated and the tail survived, but Leviathan (in-memory) evaluates
        // the same query fully, so this test passed against the broken form
        // too. Catching that regression needs a Fuseki-backed integration test.
        var repo = new InMemoryGraphRepository();
        var svc = new SavedDeckService(repo);
        var saved = await svc.SaveAsync("full", Slug, Cards(99), Bracket(), "notes", 250m);

        Assert.True(await svc.DeleteAsync(saved.Id));

        var remaining = await repo.QueryAsync(
            $"SELECT (COUNT(*) AS ?n) WHERE {{ GRAPH <{MtgVocab.SavedDecksGraphUri()}> {{ ?s ?p ?o }} }}",
            default);
        var n = int.Parse(((VDS.RDF.ILiteralNode)remaining.First()["n"]).Value);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task Deleting_an_unknown_id_reports_false()
    {
        Assert.False(await NewService().DeleteAsync("does-not-exist"));
    }

    [Fact]
    public async Task Listing_surfaces_the_bracket_level_and_budget()
    {
        var svc = NewService();
        await svc.SaveAsync("listed", Slug, Cards(), Bracket(), null, 250m);

        var row = Assert.Single(await svc.ListAsync());
        Assert.Equal("listed", row.Name);
        Assert.Equal(Slug, row.CommanderSlug);
        Assert.Equal(3, row.BracketLevel);
        Assert.Equal(250m, row.BudgetEur);
    }
}
