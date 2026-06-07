using MtgDeckEngine.Core;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Ontology;
using VDS.RDF;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class InMemoryGraphRepositoryTests
{
    [Fact]
    public async Task Seed_data_loads_and_queries()
    {
        var repo = new InMemoryGraphRepository();
        await repo.LoadTurtleAsync(OntologyResources.Ontology, namedGraphUri: null, ct: default);
        await repo.LoadTurtleAsync(OntologyResources.SeedXyris, namedGraphUri: null, ct: default);

        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
SELECT ?name WHERE {{ ?c a mtg:Card ; mtg:hasName ?name }} ORDER BY ?name";

        var rs = await repo.QueryAsync(sparql, default);
        Assert.True(rs.Count >= 3);
    }

    [Fact]
    public async Task Named_graph_isolates_triples()
    {
        var repo = new InMemoryGraphRepository();
        var ctx = new Uri(MtgVocab.CommanderContextUri("xyris-the-writhing-storm"));
        var g = new RdfGraph();
        var s = g.CreateUriNode(new Uri(MtgVocab.CardUri("test-oracle")));
        var p = g.CreateUriNode(new Uri(MtgVocab.Property("hasInclusionPct")));
        g.Assert(s, p, g.CreateLiteralNode("75",
            new Uri(VDS.RDF.Parsing.XmlSpecsHelper.XmlSchemaDataTypeDecimal)));
        await repo.WriteAsync(g, ctx, default);

        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
SELECT ?v WHERE {{ GRAPH <{ctx}> {{ ?c mtg:hasInclusionPct ?v }} }}";
        var rs = await repo.QueryAsync(sparql, default);
        Assert.Single(rs);
    }
}
