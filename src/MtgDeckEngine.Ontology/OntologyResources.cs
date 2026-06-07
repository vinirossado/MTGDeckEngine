using System.Reflection;

namespace MtgDeckEngine.Ontology;

public static class OntologyResources
{
    private static readonly Assembly Asm = typeof(OntologyResources).Assembly;
    private const string Base = "MtgDeckEngine.Ontology.";

    public static string Ontology => Read(Base + "mtg-ontology.ttl");
    public static string Shapes => Read(Base + "mtg-shapes.ttl");
    public static string SeedXyris => Read(Base + "bootstrap.seed-xyris.ttl");

    private static string Read(string name)
    {
        using var stream = Asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource not found: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
