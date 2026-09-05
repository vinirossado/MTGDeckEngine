using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Graph.Validation;
using VDS.RDF;
using VDS.RDF.Query;

namespace MtgDeckEngine.Graph.Repositories;

/// <summary>
/// Runs every outgoing graph through SHACL before it reaches the store, so
/// malformed data fails at the edge instead of becoming triples someone has
/// to find later with a query.
///
/// <para>
/// Wrapping the repository rather than calling the validator from each
/// ingestor is deliberate: there are six <c>WriteAsync</c> call sites across
/// three ingestors plus the saved-deck service, and a validator that only
/// some of them remember to call is the failure mode this class exists to
/// prevent. One choke point, no opt-outs.
/// </para>
///
/// <para>
/// Reads are delegated untouched — SHACL has nothing to say about a query.
/// </para>
/// </summary>
public sealed class ValidatingGraphRepository(
    IGraphRepository inner,
    ShaclValidator validator,
    ILogger<ValidatingGraphRepository> logger) : IGraphRepository
{
    public async Task WriteAsync(IGraph graph, Uri? namedGraphUri, CancellationToken ct)
    {
        if (graph.Triples.Count == 0) return;

        try
        {
            validator.Validate(graph);
        }
        catch (ShaclValidationException ex)
        {
            // Surface what failed before rethrowing: the exception carries the
            // report, but an aborted ingestion is a lot easier to diagnose from
            // the log than from a stack trace.
            logger.LogError(
                "SHACL rejected {Triples} triples destined for {Graph}: {Results} violation(s). First: {Message}",
                graph.Triples.Count,
                namedGraphUri?.ToString() ?? "(default)",
                ex.Report.Results.Count,
                ex.Report.Results.FirstOrDefault()?.Message?.ToString() ?? "(no message)");
            throw;
        }

        await inner.WriteAsync(graph, namedGraphUri, ct).ConfigureAwait(false);
    }

    public Task<SparqlResultSet> QueryAsync(string sparql, CancellationToken ct) =>
        inner.QueryAsync(sparql, ct);

    public Task LoadTurtleAsync(string turtle, Uri? namedGraphUri, CancellationToken ct)
    {
        var g = new VDS.RDF.Graph();
        g.LoadFromString(turtle);
        return WriteAsync(g, namedGraphUri, ct);
    }

    public Task DropGraphAsync(Uri namedGraphUri, CancellationToken ct) =>
        inner.DropGraphAsync(namedGraphUri, ct);

    /// <summary>
    /// Raw SPARQL Update passes through unvalidated: there is no data graph to
    /// hand SHACL, only an update string. The delete-then-write callers
    /// (<see cref="MutableProperties"/>) use this, and their subsequent write
    /// is validated normally.
    /// </summary>
    public Task UpdateAsync(string sparql, CancellationToken ct) =>
        inner.UpdateAsync(sparql, ct);
}
