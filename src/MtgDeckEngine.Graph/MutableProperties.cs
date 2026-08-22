using System.Text;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using VDS.RDF;

namespace MtgDeckEngine.Graph;

/// <summary>
/// Delete-before-insert for properties whose value changes over time.
///
/// <c>INSERT DATA</c> is idempotent for identical triples, which is why RDF
/// ingestion is normally safe to re-run. It is <i>not</i> idempotent for values
/// that drift: re-ingesting a card whose price moved from EUR 0.02 to EUR 279
/// leaves both triples in the graph, and every aggregate downstream then picks
/// one arbitrarily — <c>MIN(?price)</c> would keep quoting the stale EUR 0.02
/// forever. The same happens to the commander-level tournament aggregates,
/// which accumulate a new value per ingestion run.
/// </summary>
public static class MutableProperties
{
    /// <summary>Card properties that are snapshots of a moving value.</summary>
    public static readonly string[] Card =
    [
        "hasPriceEur",
        "hasPriceUsd",
    ];

    /// <summary>Commander-level tournament aggregates, recomputed each run.</summary>
    public static readonly string[] CommanderStats =
    [
        "hasTournamentEntryCount",
        "hasTournamentTopCutCount",
        "hasTournamentWinRate",
        "hasTournamentConversionRate",
        "hasMetaShare",
    ];

    /// <summary>
    /// Remove the given predicates from every subject that <paramref name="graph"/>
    /// is about to (re)assert. Call immediately before writing that graph.
    /// </summary>
    public static async Task ClearAsync(
        IGraphRepository repo,
        IGraph graph,
        IReadOnlyCollection<string> predicates,
        CancellationToken ct)
    {
        if (predicates.Count == 0) return;

        var subjects = graph.Triples
            .Select(t => t.Subject)
            .OfType<IUriNode>()
            .Select(n => n.Uri.AbsoluteUri)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (subjects.Count == 0) return;

        await ClearAsync(repo, subjects, predicates, ct).ConfigureAwait(false);
    }

    public static async Task ClearAsync(
        IGraphRepository repo,
        IReadOnlyCollection<string> subjectUris,
        IReadOnlyCollection<string> predicates,
        CancellationToken ct)
    {
        if (subjectUris.Count == 0 || predicates.Count == 0) return;

        // Chunked so a large ingest doesn't build a single enormous VALUES
        // clause — Fuseki accepts it, but Neptune caps statement size.
        const int chunkSize = 500;
        foreach (var chunk in subjectUris.Chunk(chunkSize))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"PREFIX mtg: <{MtgVocab.Namespace}>");
            sb.AppendLine("DELETE { ?s ?p ?o }");
            sb.AppendLine("WHERE {");
            sb.AppendLine("  VALUES ?s { " +
                string.Join(" ", chunk.Select(u => $"<{u}>")) + " }");
            sb.AppendLine("  VALUES ?p { " +
                string.Join(" ", predicates.Select(p => $"mtg:{p}")) + " }");
            sb.AppendLine("  ?s ?p ?o .");
            sb.AppendLine("}");
            await repo.UpdateAsync(sb.ToString(), ct).ConfigureAwait(false);
        }
    }
}
