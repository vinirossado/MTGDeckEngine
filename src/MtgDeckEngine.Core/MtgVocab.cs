using System.Globalization;
using System.Text;

namespace MtgDeckEngine.Core;

public static class MtgVocab
{
    public const string Namespace = "http://example.org/mtg#";

    public static string Class(string local) => Namespace + local;
    public static string Property(string local) => Namespace + local;
    public static string CardUri(string oracleId) => $"{Namespace}card/{oracleId}";
    public static string CommanderUri(string slug) => $"{Namespace}commander/{slug}";
    public static string DeckUri(string source, string sourceId) => $"{Namespace}deck/{source}/{sourceId}";
    public static string TournamentUri(string source, string id) => $"{Namespace}tournament/{source}/{id}";
    public static string TournamentEntryUri(string source, string tournamentId, string playerKey)
        => $"{Namespace}entry/{source}/{tournamentId}/{playerKey}";
    public static string ColorUri(string code) => $"{Namespace}color/{code}";
    public static string CategoryUri(string slug) => $"{Namespace}category/{slug}";
    public static string CommanderContextUri(string slug) => $"{Namespace}context/{slug}";

    /// <summary>A deck the user explicitly saved, as opposed to one ingested
    /// from a tournament source.</summary>
    public static string SavedDeckUri(string id) => $"{Namespace}deck/saved/{id}";

    /// <summary>
    /// Named graph holding every saved deck. Kept out of the default graph so
    /// user decks never pollute tournament aggregates — a saved deck is not a
    /// tournament result and must not be counted as one by the win-rate queries.
    /// </summary>
    public static string SavedDecksGraphUri() => $"{Namespace}graph/saved-decks";

    /// <summary>
    /// Card name → EDHREC-style slug, e.g. "Atraxa, Praetors' Voice" →
    /// <c>atraxa-praetors-voice</c>. Must match EDHREC exactly: it keys their
    /// commander pages, the Scryfall bulk cache's slug index, and every
    /// commander URI in the graph.
    ///
    /// Three rules that a naive character map gets wrong:
    /// <list type="bullet">
    /// <item>Double-faced cards slug on the front face alone. "Kefka, Court Mage
    /// // Kefka, Ruler of Ruin" is <c>kefka-court-mage</c>, not a slug carrying
    /// a "//" — which is not even legal in a URI path segment.</item>
    /// <item>Apostrophes are dropped, not turned into separators: "Clachan's
    /// Heart" is <c>clachans-heart</c>. (Replacing them happened to work for
    /// "Praetors' Voice" only because the following space collapsed the pair.)</item>
    /// <item>Diacritics are folded: "Lord of the Nazgûl" is
    /// <c>lord-of-the-nazgul</c>.</item>
    /// </list>
    ///
    /// Anything else outside [a-z0-9] becomes a separator. The previous version
    /// passed unknown characters through untouched, which put "//", "&amp;" and
    /// "û" straight into 47 commander URIs.
    /// </summary>
    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        // Front face only, before anything else.
        var idx = input.IndexOf(" // ", StringComparison.Ordinal);
        var name = idx > 0 ? input[..idx] : input;

        // Decompose so diacritics become separate combining marks we can drop.
        var decomposed = name.ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;                                   // the accent itself
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else if (ch is '\'' or '\u2019' or '"')
                continue;                                   // dropped, not separated
            else
                sb.Append('-');
        }

        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }
}
