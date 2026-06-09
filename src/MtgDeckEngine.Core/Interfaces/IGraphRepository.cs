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
}
