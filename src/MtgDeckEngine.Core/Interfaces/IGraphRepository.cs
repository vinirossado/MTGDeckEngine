using VDS.RDF;
using VDS.RDF.Query;

namespace MtgDeckEngine.Core.Interfaces;

public interface IGraphRepository
{
    Task WriteAsync(IGraph graph, Uri? namedGraphUri, CancellationToken ct);

    Task<SparqlResultSet> QueryAsync(string sparql, CancellationToken ct);

    Task LoadTurtleAsync(string turtle, Uri? namedGraphUri, CancellationToken ct);

    /// <summary>
    /// Drop all triples in the given named graph. Used to overwrite
    /// snapshot-style data (e.g. EDHREC inclusion %, which drifts each
    /// time the source meta updates and would otherwise accumulate
    /// duplicate predicate values across re-ingests).
    /// </summary>
    Task DropGraphAsync(Uri namedGraphUri, CancellationToken ct);

    /// <summary>
    /// Run an arbitrary SPARQL 1.1 Update. Needed for targeted DELETE/WHERE —
    /// <see cref="DropGraphAsync"/> is too blunt when only one subject inside a
    /// shared named graph should go.
    /// </summary>
    Task UpdateAsync(string sparqlUpdate, CancellationToken ct);
}
