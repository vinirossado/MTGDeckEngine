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

    public static string Slugify(string input)
    {
        var lower = input.ToLowerInvariant();
        var chars = lower.Select(c =>
            c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c
            : c is ' ' or ',' or '\'' or '"' or '.' or ':' ? '-'
            : c).ToArray();
        var s = new string(chars);
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Trim('-');
    }
}
