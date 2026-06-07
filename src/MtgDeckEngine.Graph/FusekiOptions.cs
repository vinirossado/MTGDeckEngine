namespace MtgDeckEngine.Graph;

public sealed class FusekiOptions
{
    public const string SectionName = "Fuseki";

    /// <summary>SPARQL Query endpoint, e.g. http://localhost:3030/mtg/sparql</summary>
    public string QueryEndpoint { get; set; } = "http://localhost:3030/mtg/sparql";

    /// <summary>SPARQL Update endpoint, e.g. http://localhost:3030/mtg/update</summary>
    public string UpdateEndpoint { get; set; } = "http://localhost:3030/mtg/update";
}
