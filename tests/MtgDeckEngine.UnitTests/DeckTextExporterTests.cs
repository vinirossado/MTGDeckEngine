using MtgDeckEngine.Core.Models;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class DeckTextExporterTests
{
    [Fact]
    public void Duplicates_collapse_into_counts()
    {
        var text = DeckTextExporter.ToText(
            ["Island", "Island", "Island", "Sol Ring", "Mountain"]);

        Assert.Contains("3 Island\n", text);
        Assert.Contains("1 Sol Ring\n", text);
        Assert.Contains("1 Mountain\n", text);
    }

    [Fact]
    public void Lines_are_alphabetical()
    {
        var text = DeckTextExporter.ToText(["Sol Ring", "Ancient Den", "Mountain"]);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["1 Ancient Den", "1 Mountain", "1 Sol Ring"], lines);
    }

    [Fact]
    public void Commander_goes_in_a_trailing_block_after_a_blank_line()
    {
        // The blank line is what tells Moxfield/Archidekt the last block is the
        // command zone; without it the commander imports as a maindeck card.
        var text = DeckTextExporter.ToText(["Sol Ring"], "Breya, Etherium Shaper");

        Assert.Equal("1 Sol Ring\n\n1 Breya, Etherium Shaper\n", text);
    }

    [Fact]
    public void No_commander_means_no_trailing_block()
    {
        var text = DeckTextExporter.ToText(["Sol Ring"]);
        Assert.Equal("1 Sol Ring\n", text);
        Assert.DoesNotContain("\n\n", text);
    }

    [Fact]
    public void Blank_and_whitespace_names_are_skipped()
    {
        var text = DeckTextExporter.ToText(["Sol Ring", "", "   ", "Island"]);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Card_recommendations_export_by_name()
    {
        var cards = new[]
        {
            new CardRecommendation("a", "Island", null, null, null, 0m),
            new CardRecommendation("a", "Island", null, null, null, 0m),
            new CardRecommendation("b", "Sol Ring", null, null, null, 1.59m),
        };

        var text = DeckTextExporter.ToText(cards, "Breya, Etherium Shaper");

        Assert.Equal("2 Island\n1 Sol Ring\n\n1 Breya, Etherium Shaper\n", text);
    }
}
