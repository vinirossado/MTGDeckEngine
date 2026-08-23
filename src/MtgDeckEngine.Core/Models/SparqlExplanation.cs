namespace MtgDeckEngine.Core.Models;

/// <summary>
/// One SPARQL query an endpoint would run, with enough context to understand it
/// out of context. The queries are generated from request parameters — budget,
/// bracket, category filters — so a static file in <c>queries/</c> cannot stand
/// in for the query your particular request actually issues.
/// </summary>
public sealed record SparqlExplanation(
    string Name,
    string Purpose,
    string Sparql)
{
    /// <summary>
    /// Renders as a runnable .sparql file: the purpose as a leading comment,
    /// then the query. Comments are legal SPARQL, so the output can be piped
    /// straight to a file and executed.
    /// </summary>
    public string ToAnnotatedSparql()
    {
        var header = string.Join('\n',
            Purpose.Split('\n').Select(l => $"# {l.TrimEnd()}".TrimEnd()));
        return $"# ===== {Name} =====\n{header}\n\n{Sparql.Trim()}\n";
    }

    public static string ToDocument(IEnumerable<SparqlExplanation> queries)
        => string.Join("\n\n", queries.Select(q => q.ToAnnotatedSparql()));
}
