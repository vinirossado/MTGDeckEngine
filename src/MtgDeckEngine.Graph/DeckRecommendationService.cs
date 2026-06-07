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
            var topCut = Dec(row, "topCutCount");
            list.Add(new CardRecommendation(
                OracleId:            Str(row, "oracleId") ?? "",
                Name:                Str(row, "name") ?? "",
                Category:            Str(row, "categoryLabel"),
                InclusionPct:        Dec(row, "inclusion"),
                SynergyScore:        Dec(row, "synergy"),
                PriceEur:            Dec(row, "priceEur"),
                TopCutAppearances:   topCut.HasValue ? (int)topCut.Value : null));
        }
        return list;
    }

    public async Task<CommanderMeta> GetCommanderMetaAsync(
        string commanderSlug,
        CancellationToken ct)
    {
        var commanderUri = MtgVocab.CommanderUri(commanderSlug);
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>

SELECT ?entries ?topCuts ?winRate ?conversion ?metaShare
       (COUNT(DISTINCT ?top4Deck) AS ?top4)
       (COUNT(DISTINCT ?top16Deck) AS ?top16)
       (MAX(?date) AS ?latestDate)
WHERE {{
  OPTIONAL {{ <{commanderUri}> mtg:hasTournamentEntryCount    ?entries }}
  OPTIONAL {{ <{commanderUri}> mtg:hasTournamentTopCutCount   ?topCuts }}
  OPTIONAL {{ <{commanderUri}> mtg:hasTournamentWinRate       ?winRate }}
  OPTIONAL {{ <{commanderUri}> mtg:hasTournamentConversionRate ?conversion }}
  OPTIONAL {{ <{commanderUri}> mtg:hasMetaShare               ?metaShare }}
  OPTIONAL {{
    ?entry4 mtg:hasPlacement ?p4 ; mtg:hasDeck ?top4Deck .
    ?top4Deck mtg:hasCommander <{commanderUri}> .
    FILTER (?p4 <= 4)
  }}
  OPTIONAL {{
    ?entry16 mtg:hasPlacement ?p16 ; mtg:hasDeck ?top16Deck ; mtg:inTournament ?t .
    ?top16Deck mtg:hasCommander <{commanderUri}> .
    ?t mtg:hasDate ?date .
    FILTER (?p16 <= 16)
  }}
}}
GROUP BY ?entries ?topCuts ?winRate ?conversion ?metaShare";

        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        if (rs.Count == 0)
        {
            return new CommanderMeta(commanderSlug, 0, 0, null, null, null, 0, 0, null);
        }
        var row = rs.First();
        DateOnly? latest = null;
        var latestStr = Str(row, "latestDate");
        if (latestStr is not null
            && DateOnly.TryParse(latestStr, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            latest = parsed;
        return new CommanderMeta(
            CommanderSlug:        commanderSlug,
            TournamentEntryCount: (int)(Dec(row, "entries") ?? 0m),
            TopCutCount:          (int)(Dec(row, "topCuts") ?? 0m),
            WinRate:              Dec(row, "winRate"),
            ConversionRate:       Dec(row, "conversion"),
            MetaShare:            Dec(row, "metaShare"),
            Top4DeckCount:        (int)(Dec(row, "top4") ?? 0m),
            Top16DeckCount:       (int)(Dec(row, "top16") ?? 0m),
            LatestTopCutDate:     latest);
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
        var commander = MtgVocab.CommanderUri(commanderSlug);
        var maxPrice = f.MaxPriceEur ?? decimal.MaxValue;
        var minIncl = f.MinInclusionPct ?? 0m;
        var minSyn  = f.MinSynergy ?? decimal.MinValue;
        var lim     = Math.Clamp(f.Limit, 1, 500);
        var tourMode = f.Source == RecommendationSource.Tournament;
        var allMode  = f.Source == RecommendationSource.All;
        var includeTournamentCount = tourMode || allMode
                                     || f.MinTopCutAppearances is not null
                                     || f.MaxPlacement is not null;

        var sb = new StringBuilder();
        sb.AppendLine($"PREFIX mtg:  <{MtgVocab.Namespace}>");
        sb.AppendLine("PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>");
        sb.AppendLine("PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>");
        sb.AppendLine();
        sb.AppendLine("SELECT ?oracleId ?name ?categoryLabel ?inclusion ?synergy ?priceEur ?topCutCount WHERE {");

        // EDHREC context graph — required for Edhrec/All sources, optional for
        // Tournament source so we still find cards that EDHREC doesn't list.
        if (tourMode) sb.AppendLine("  OPTIONAL {");
        sb.AppendLine($"  GRAPH <{ctx}> {{");
        sb.AppendLine("    ?card mtg:hasInclusionPct ?inclusion .");
        sb.AppendLine("    OPTIONAL { ?card mtg:hasSynergyScore ?synergy }");
        sb.AppendLine("    OPTIONAL { ?card mtg:inCategory ?cat . OPTIONAL { ?cat rdfs:label ?categoryLabel } }");
        sb.AppendLine("  }");
        if (tourMode) sb.AppendLine("  }");

        sb.AppendLine("  ?card mtg:hasOracleId ?oracleId ;");
        sb.AppendLine("        mtg:hasName     ?name ;");
        sb.AppendLine("        mtg:hasTypeLine ?typeLine .");
        sb.AppendLine("  OPTIONAL { ?card mtg:hasPriceEur ?priceEur }");

        // Tournament appearance subquery — counts distinct top-cut decks per card.
        if (includeTournamentCount)
        {
            var wrap = tourMode ? "" : "OPTIONAL ";
            sb.AppendLine($"  {wrap}{{");
            sb.AppendLine("    SELECT ?card (COUNT(DISTINCT ?deck) AS ?topCutCount) WHERE {");
            sb.AppendLine("      ?entry mtg:hasPlacement ?p ; mtg:hasDeck ?deck .");
            sb.AppendLine($"      ?deck  mtg:hasCommander <{commander}> ; mtg:containsCard ?card .");
            if (f.MaxPlacement is not null)
                sb.AppendLine($"      FILTER (?p <= {f.MaxPlacement.Value})");
            sb.AppendLine("    } GROUP BY ?card");
            sb.AppendLine("  }");
        }

        // Filters.
        sb.AppendLine($"  FILTER (!BOUND(?priceEur) || ?priceEur <= \"{Fmt(maxPrice)}\"^^xsd:decimal)");
        if (!tourMode)
            sb.AppendLine($"  FILTER (BOUND(?inclusion) && ?inclusion >= \"{Fmt(minIncl)}\"^^xsd:decimal)");
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
        if (f.MinTopCutAppearances is not null)
            sb.AppendLine($"  FILTER (BOUND(?topCutCount) && ?topCutCount >= {f.MinTopCutAppearances.Value})");
        sb.AppendLine("}");

        sb.AppendLine(tourMode
            ? "ORDER BY DESC(?topCutCount) DESC(?inclusion)"
            : allMode
                ? "ORDER BY DESC(?topCutCount) DESC(?inclusion) DESC(?synergy)"
                : "ORDER BY DESC(?inclusion) DESC(?synergy)");
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
