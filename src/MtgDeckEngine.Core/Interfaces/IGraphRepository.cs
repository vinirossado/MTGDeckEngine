using VDS.RDF;
using VDS.RDF.Query;

namespace MtgDeckEngine.Core.Interfaces;

public interface IGraphRepository
{
    Task WriteAsync(IGraph graph, Uri? namedGraphUri, CancellationToken ct);

    Task<SparqlResultSet> QueryAsync(string sparql, CancellationToken ct);

    Task LoadTurtleAsync(string turtle, Uri? namedGraphUri, CancellationToken ct);
}
