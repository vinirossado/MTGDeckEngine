using System.Reflection;

namespace MtgDeckEngine.Graph;

public static class SparqlQueries
{
    private static readonly Assembly Asm = typeof(SparqlQueries).Assembly;

    public static string RecommendationsByInclusionAndBudget { get; } =
        Read("MtgDeckEngine.Graph.Queries.RecommendationsByInclusionAndBudget.sparql");

    public static string CardFrequency { get; } =
        Read("MtgDeckEngine.Graph.Queries.CardFrequency.sparql");

    public static string BudgetFilter { get; } =
        Read("MtgDeckEngine.Graph.Queries.BudgetFilter.sparql");

    private static string Read(string name)
    {
        using var stream = Asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded SPARQL not found: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
