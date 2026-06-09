using MtgDeckEngine.Core.Interfaces;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using VDS.RDF.Query.Datasets;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.Graph.Repositories;

/// <summary>
/// In-memory triplestore backed by dotNetRDF's <see cref="TripleStore"/>
/// and Leviathan SPARQL engine. Intended for unit tests and offline development.
/// </summary>
public sealed class InMemoryGraphRepository : IGraphRepository
{
    private readonly TripleStore _store = new();
    private readonly LeviathanQueryProcessor _processor;
    private readonly SparqlQueryParser _parser = new();
    private readonly object _lock = new();

    public InMemoryGraphRepository()
    {
        _processor = new LeviathanQueryProcessor(new InMemoryDataset(_store, unionDefaultGraph: true));
    }

    public TripleStore Store => _store;

    public Task WriteAsync(IGraph graph, Uri? namedGraphUri, CancellationToken ct)
    {
        lock (_lock)
        {
            var target = ResolveTarget(namedGraphUri);
            target.Merge(graph);
        }
        return Task.CompletedTask;
    }

    public Task LoadTurtleAsync(string turtle, Uri? namedGraphUri, CancellationToken ct)
    {
        var g = new RdfGraph();
        g.LoadFromString(turtle, new TurtleParser());
        return WriteAsync(g, namedGraphUri, ct);
    }

    public Task DropGraphAsync(Uri namedGraphUri, CancellationToken ct)
    {
        lock (_lock)
        {
            var name = new UriNode(namedGraphUri);
            if (_store.HasGraph(name))
                _store.Remove(name);
        }
        return Task.CompletedTask;
    }

    public Task<SparqlResultSet> QueryAsync(string sparql, CancellationToken ct)
    {
        var query = _parser.ParseFromString(sparql);
        var resultSet = new SparqlResultSet();
        _processor.ProcessQuery(
            rdfHandler: null,
            resultsHandler: new VDS.RDF.Parsing.Handlers.ResultSetHandler(resultSet),
            query: query);
        return Task.FromResult(resultSet);
    }

    private IGraph ResolveTarget(Uri? namedGraphUri)
    {
        if (namedGraphUri is null)
        {
            if (_store.Graphs.Count == 0)
            {
                var def = new RdfGraph();
                _store.Add(def);
                return def;
            }
            return _store.Graphs.First();
        }

        var name = new UriNode(namedGraphUri);
        if (_store.HasGraph(name)) return _store[name];
        var g = new RdfGraph(name);
        _store.Add(g);
        return g;
    }
}
