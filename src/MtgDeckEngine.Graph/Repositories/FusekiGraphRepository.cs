using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Core.Interfaces;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using VDS.RDF.Update;
using VDS.RDF.Writing.Formatting;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.Graph.Repositories;

/// <summary>
/// Talks SPARQL 1.1 over HTTP. Works against Fuseki locally and Neptune in prod —
/// only the endpoint URIs change.
/// </summary>
public sealed class FusekiGraphRepository : IGraphRepository
{
    private readonly SparqlQueryClient _queryClient;
    private readonly SparqlUpdateClient _updateClient;
    private readonly ILogger<FusekiGraphRepository> _logger;
    private static readonly NTriplesFormatter NTriples = new();

    public FusekiGraphRepository(
        HttpClient httpClient,
        IOptions<FusekiOptions> options,
        ILogger<FusekiGraphRepository> logger)
    {
        var opts = options.Value;
        _queryClient = new SparqlQueryClient(httpClient, new Uri(opts.QueryEndpoint));
        _updateClient = new SparqlUpdateClient(httpClient, new Uri(opts.UpdateEndpoint));
        _logger = logger;
    }

    public async Task<SparqlResultSet> QueryAsync(string sparql, CancellationToken ct)
    {
        return await _queryClient.QueryWithResultSetAsync(sparql, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fuseki posts SPARQL Update as a form, and Jetty rejects form bodies over
    /// 20 MB with "form too large" — surfaced to the client as a bare HTTP 500.
    /// Cap each request well under that; the graph is split across as many
    /// requests as it takes.
    /// </summary>
    private const int MaxBytesPerUpdate = 8 * 1024 * 1024;

    public async Task WriteAsync(IGraph graph, Uri? namedGraphUri, CancellationToken ct)
    {
        if (graph.Triples.Count == 0) return;

        // Chunk by serialised size rather than triple count: triples vary from
        // ~120 bytes to several KB (long oracle text, decklist URLs), so a
        // fixed triple budget either wastes requests or still blows the limit.
        var batch = new List<Triple>();
        var batchBytes = 0;
        var flushes = 0;

        foreach (var triple in graph.Triples)
        {
            var size = NTriples.Format(triple).Length + 1;
            if (batch.Count > 0 && batchBytes + size > MaxBytesPerUpdate)
            {
                await FlushAsync(batch, namedGraphUri, ct).ConfigureAwait(false);
                flushes++;
                batch.Clear();
                batchBytes = 0;
            }
            batch.Add(triple);
            batchBytes += size;
        }
        if (batch.Count > 0)
        {
            await FlushAsync(batch, namedGraphUri, ct).ConfigureAwait(false);
            flushes++;
        }

        if (flushes > 1)
            _logger.LogDebug(
                "SPARQL UPDATE split into {Flushes} requests ({Triples} triples) → {Graph}",
                flushes, graph.Triples.Count, namedGraphUri?.ToString() ?? "(default)");
    }

    private async Task FlushAsync(
        IReadOnlyCollection<Triple> triples, Uri? namedGraphUri, CancellationToken ct)
    {
        var update = BuildInsertData(triples, namedGraphUri);
        _logger.LogDebug("SPARQL UPDATE: {Bytes} bytes, {Triples} triples → {Graph}",
            update.Length, triples.Count, namedGraphUri?.ToString() ?? "(default)");
        await _updateClient.UpdateAsync(update, ct).ConfigureAwait(false);
    }

    public async Task LoadTurtleAsync(string turtle, Uri? namedGraphUri, CancellationToken ct)
    {
        var g = new RdfGraph();
        g.LoadFromString(turtle, new TurtleParser());
        await WriteAsync(g, namedGraphUri, ct).ConfigureAwait(false);
    }

    public async Task DropGraphAsync(Uri namedGraphUri, CancellationToken ct)
    {
        // SILENT so it's a no-op (no error) when the graph doesn't exist yet —
        // i.e. on the first ingest run for a commander.
        var update = $"DROP SILENT GRAPH <{namedGraphUri.AbsoluteUri}>";
        _logger.LogDebug("SPARQL UPDATE: {Update}", update);
        await _updateClient.UpdateAsync(update, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(string sparqlUpdate, CancellationToken ct)
    {
        _logger.LogDebug("SPARQL UPDATE: {Bytes} bytes", sparqlUpdate.Length);
        await _updateClient.UpdateAsync(sparqlUpdate, ct).ConfigureAwait(false);
    }

    private static string BuildInsertData(
        IReadOnlyCollection<Triple> triples, Uri? namedGraphUri)
    {
        using var writer = new StringWriter();
        writer.Write("INSERT DATA { ");
        if (namedGraphUri is not null)
        {
            writer.Write("GRAPH <");
            writer.Write(namedGraphUri.AbsoluteUri);
            writer.Write("> { ");
        }
        foreach (var t in triples)
        {
            writer.Write(NTriples.Format(t));
            writer.Write(' ');
        }
        if (namedGraphUri is not null) writer.Write("} ");
        writer.Write('}');
        return writer.ToString();
    }
}
