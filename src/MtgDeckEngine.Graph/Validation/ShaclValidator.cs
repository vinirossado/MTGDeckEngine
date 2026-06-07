using MtgDeckEngine.Ontology;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.Graph.Validation;

public sealed class ShaclValidator
{
    private readonly ShapesGraph _shapes;

    public ShaclValidator() : this(OntologyResources.Shapes) { }

    public ShaclValidator(string shapesTurtle)
    {
        var g = new RdfGraph();
        g.LoadFromString(shapesTurtle, new TurtleParser());
        _shapes = new ShapesGraph(g);
    }

    public void Validate(IGraph dataGraph)
    {
        var report = _shapes.Validate(dataGraph);
        if (!report.Conforms)
            throw new ShaclValidationException(report);
    }
}
