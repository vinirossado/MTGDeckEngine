using System.Globalization;
using System.Text;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using VDS.RDF;

namespace MtgDeckEngine.Graph;

public sealed class DeckRecommendationService(IGraphRepository repo) : IDeckRecommendationService
{
    public async Task<IReadOnlyList<CardRecommendation>> GetRecommendationsAsync(
        string commanderSlug,
        RecommendationFilter filter,
        CancellationToken ct)
    {
        var sparql = BuildQuery(commanderSlug, filter);
        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        var list = new List<CardRecommendation>(rs.Count);
        foreach (var row in rs)
        {
            list.Add(new CardRecommendation(
                OracleId:     Str(row, "oracleId") ?? "",
                Name:         Str(row, "name") ?? "",
                Category:     Str(row, "categoryLabel"),
                InclusionPct: Dec(row, "inclusion"),
                SynergyScore: Dec(row, "synergy"),
                PriceEur:     Dec(row, "priceEur")));
        }
        return list;
    }

    public async Task<BudgetDeck> BuildBudgetDeckAsync(
        string commanderSlug,
        decimal totalBudgetEur,
        RecommendationFilter filter,
        CancellationToken ct)
    {
        // Greedy: fetch a wider pool than 99 so the cap can drop expensive misses.
        // 300 covers all practical cases; the SPARQL ORDER BY already favours
        // the highest-inclusion cards.
        var fatFilter = filter with { Limit = 300 };
        var pool = await GetRecommendationsAsync(commanderSlug, fatFilter, ct);

        var chosen = new List<CardRecommendation>(99);
        var seenOracleIds = new HashSet<string>(StringComparer.Ordinal);
        decimal running = 0m;

        foreach (var card in pool)
        {
            if (chosen.Count >= 99) break;
            if (!seenOracleIds.Add(card.OracleId)) continue;
            var price = card.PriceEur ?? 0m;
            if (running + price > totalBudgetEur) continue;
            chosen.Add(card);
            running += price;
        }
        return new BudgetDeck(commanderSlug, running, chosen.Count, chosen);
    }

    private static string BuildQuery(string commanderSlug, RecommendationFilter f)
    {
        var ctx = MtgVocab.CommanderContextUri(commanderSlug);
        var maxPrice = f.MaxPriceEur ?? decimal.MaxValue;
        var minIncl = f.MinInclusionPct ?? 0m;
        var minSyn  = f.MinSynergy ?? decimal.MinValue;
        var lim     = Math.Clamp(f.Limit, 1, 500);

        var sb = new StringBuilder();
        sb.AppendLine($"PREFIX mtg:  <{MtgVocab.Namespace}>");
        sb.AppendLine("PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>");
        sb.AppendLine("PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>");
        sb.AppendLine();
        sb.AppendLine("SELECT ?oracleId ?name ?categoryLabel ?inclusion ?synergy ?priceEur WHERE {");
        sb.AppendLine($"  GRAPH <{ctx}> {{");
        sb.AppendLine("    ?card mtg:hasInclusionPct ?inclusion .");
        sb.AppendLine("    OPTIONAL { ?card mtg:hasSynergyScore ?synergy }");
        sb.AppendLine("    OPTIONAL { ?card mtg:inCategory ?cat . OPTIONAL { ?cat rdfs:label ?categoryLabel } }");
        sb.AppendLine("  }");
        sb.AppendLine("  ?card mtg:hasOracleId ?oracleId ;");
        sb.AppendLine("        mtg:hasName     ?name ;");
        sb.AppendLine("        mtg:hasTypeLine ?typeLine .");
        sb.AppendLine("  OPTIONAL { ?card mtg:hasPriceEur ?priceEur }");
        sb.AppendLine($"  FILTER (!BOUND(?priceEur) || ?priceEur <= \"{Fmt(maxPrice)}\"^^xsd:decimal)");
        sb.AppendLine($"  FILTER (?inclusion >= \"{Fmt(minIncl)}\"^^xsd:decimal)");
        if (f.MinSynergy is not null)
            sb.AppendLine($"  FILTER (BOUND(?synergy) && ?synergy >= \"{Fmt(minSyn)}\"^^xsd:decimal)");
        if (f.ExcludeLands)
            sb.AppendLine("  FILTER (!CONTAINS(?typeLine, \"Land\"))");
        if (f.ExcludeBasicLands)
            sb.AppendLine("  FILTER (!CONTAINS(?typeLine, \"Basic Land\"))");
        if (f.ExcludeCategories is { Count: > 0 })
        {
            var blocklist = string.Join(", ", f.ExcludeCategories.Select(c => $"\"{c}\""));
            sb.AppendLine($"  FILTER (!BOUND(?categoryLabel) || !(?categoryLabel IN ({blocklist})))");
        }
        if (f.IncludeOnlyCategories is { Count: > 0 })
        {
            var allowlist = string.Join(", ", f.IncludeOnlyCategories.Select(c => $"\"{c}\""));
            sb.AppendLine($"  FILTER (BOUND(?categoryLabel) && ?categoryLabel IN ({allowlist}))");
        }
        sb.AppendLine("}");
        sb.AppendLine("ORDER BY DESC(?inclusion) DESC(?synergy)");
        sb.AppendLine($"LIMIT {lim}");
        return sb.ToString();
    }

    private static string Fmt(decimal d) => d.ToString("0.############", CultureInfo.InvariantCulture);

    private static string? Str(VDS.RDF.Query.ISparqlResult row, string var)
        => row.HasBoundValue(var) && row[var] is ILiteralNode lit ? lit.Value : null;

    private static decimal? Dec(VDS.RDF.Query.ISparqlResult row, string var)
    {
        if (!row.HasBoundValue(var) || row[var] is not ILiteralNode lit) return null;
        return decimal.TryParse(lit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }
}
