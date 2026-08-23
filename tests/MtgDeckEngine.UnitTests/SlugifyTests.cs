using MtgDeckEngine.Core;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class SlugifyTests
{
    [Theory]
    // The slug format CLAUDE.md documents for EDHREC.
    [InlineData("Atraxa, Praetors' Voice", "atraxa-praetors-voice")]
    [InlineData("Xyris, the Writhing Storm", "xyris-the-writhing-storm")]
    // Double-faced cards slug on the front face. A "//" is not legal in a URI
    // path segment, and EDHREC does not use one.
    [InlineData("Kefka, Court Mage // Kefka, Ruler of Ruin", "kefka-court-mage")]
    [InlineData("Bruce Banner // The Incredible Hulk", "bruce-banner")]
    // Apostrophes are dropped, not turned into separators. Mid-word is where a
    // character map gets it wrong: "clachan-s-heart" is not the EDHREC slug.
    [InlineData("Brigid, Clachan's Heart", "brigid-clachans-heart")]
    [InlineData("Ludevic's Opus", "ludevics-opus")]
    // Curly apostrophes too — they turn up in pasted names.
    [InlineData("Ludevic’s Opus", "ludevics-opus")]
    // Diacritics fold rather than surviving into the URI.
    [InlineData("Lord of the Nazgûl", "lord-of-the-nazgul")]
    [InlineData("Círdan the Shipwright", "cirdan-the-shipwright")]
    // Ampersands and other punctuation become separators.
    [InlineData("Raph & Mikey, Troublemakers", "raph-mikey-troublemakers")]
    [InlineData("Rograkh, Son of Rohgahh", "rograkh-son-of-rohgahh")]
    // Already-slugged input is unchanged, so the function is safe to reapply.
    [InlineData("kefka-court-mage", "kefka-court-mage")]
    public void Matches_the_edhrec_slug(string name, string expected)
        => Assert.Equal(expected, MtgVocab.Slugify(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Degenerate_input_never_yields_a_broken_slug(string input)
    {
        var slug = MtgVocab.Slugify(input);
        Assert.DoesNotContain("--", slug);
        Assert.False(slug.StartsWith('-') || slug.EndsWith('-'));
    }

    [Fact]
    public void Output_is_always_safe_in_a_uri_path_segment()
    {
        // The old version passed unknown characters straight through, which put
        // "//", "&" and "û" into 47 commander URIs.
        string[] names =
        [
            "Kefka, Court Mage // Kefka, Ruler of Ruin",
            "Raph & Mikey, Troublemakers",
            "Lord of the Nazgûl",
            "Brigid, Clachan's Heart",
            "Slicer, Hired Muscle // Slicer, High-Speed Antagonist",
        ];

        foreach (var n in names)
        {
            var slug = MtgVocab.Slugify(n);
            Assert.Matches("^[a-z0-9-]+$", slug);
            // And it must survive being built into a URI unchanged.
            var uri = new Uri(MtgVocab.CommanderUri(slug));
            Assert.EndsWith(slug, uri.ToString(), StringComparison.Ordinal);
        }
    }
}
