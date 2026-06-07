using VDS.RDF.Shacl.Validation;

namespace MtgDeckEngine.Graph.Validation;

public sealed class ShaclValidationException(Report report)
    : Exception("Graph failed SHACL validation; see Report for details.")
{
    public Report Report { get; } = report;
}
