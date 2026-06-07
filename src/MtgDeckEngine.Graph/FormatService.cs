using System.Globalization;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using VDS.RDF;

namespace MtgDeckEngine.Graph;

public sealed class FormatService(IGraphRepository repo) : IFormatService
{
    public async Task<IReadOnlyList<FormatSummary>> ListFormatsAsync(CancellationToken ct)
    {
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
SELECT ?format
       (COUNT(DISTINCT ?t) AS ?tournaments)
       (COUNT(DISTINCT ?deck) AS ?decks)
       (COUNT(DISTINCT ?entry) AS ?entries)
WHERE {{
  ?t a mtg:Tournament ; mtg:hasFormat ?format .
  OPTIONAL {{ ?entry mtg:inTournament ?t ; mtg:hasDeck ?deck }}
}}
GROUP BY ?format
ORDER BY DESC(?tournaments)";

        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        var list = new List<FormatSummary>();
        foreach (var row in rs)
        {
            list.Add(new FormatSummary(
                Format:          Str(row, "format") ?? "UNKNOWN",
                TournamentCount: (int)(Dec(row, "tournaments") ?? 0m),
                DeckCount:       (int)(Dec(row, "decks") ?? 0m),
                EntryCount:      (int)(Dec(row, "entries") ?? 0m)));
        }
        return list;
    }

    public async Task<FormatMeta> GetFormatMetaAsync(string format, CancellationToken ct)
    {
        var fmt = EscapeLiteral(format);
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
SELECT (COUNT(DISTINCT ?t) AS ?tournaments)
       (COUNT(DISTINCT ?deck) AS ?decks)
       (COUNT(DISTINCT ?entry) AS ?entries)
       (COUNT(DISTINCT ?top4Deck) AS ?top4)
       (COUNT(DISTINCT ?top16Deck) AS ?top16)
       (MAX(?date) AS ?latest)
WHERE {{
  ?t a mtg:Tournament ; mtg:hasFormat ""{fmt}"" .
  OPTIONAL {{ ?t mtg:hasDate ?date }}
  OPTIONAL {{ ?entry mtg:inTournament ?t ; mtg:hasDeck ?deck }}
  OPTIONAL {{
    ?e4 mtg:inTournament ?t ; mtg:hasPlacement ?p4 ; mtg:hasDeck ?top4Deck .
    FILTER (?p4 <= 4)
  }}
  OPTIONAL {{
    ?e16 mtg:inTournament ?t ; mtg:hasPlacement ?p16 ; mtg:hasDeck ?top16Deck .
    FILTER (?p16 <= 16)
  }}
}}";

        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        if (rs.Count == 0)
            return new FormatMeta(format, 0, 0, 0, 0, 0, null);
        var row = rs.First();
        DateOnly? latest = null;
        var latestStr = Str(row, "latest");
        if (latestStr is not null
            && DateOnly.TryParse(latestStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            latest = parsed;
        return new FormatMeta(
            Format:                 format,
            TournamentCount:        (int)(Dec(row, "tournaments") ?? 0m),
            DeckCount:              (int)(Dec(row, "decks") ?? 0m),
            EntryCount:             (int)(Dec(row, "entries") ?? 0m),
            Top4DeckCount:          (int)(Dec(row, "top4") ?? 0m),
            Top16DeckCount:         (int)(Dec(row, "top16") ?? 0m),
            LatestTournamentDate:   latest);
    }

    public async Task<IReadOnlyList<FormatStaple>> GetFormatStaplesAsync(
        string format,
        decimal? maxPriceEur,
        int? maxPlacement,
        int minDeckCount,
        int limit,
        CancellationToken ct)
    {
        var fmt = EscapeLiteral(format);
        var lim = Math.Clamp(limit, 1, 500);
        var maxPrice = maxPriceEur ?? decimal.MaxValue;
        var placementFilter = maxPlacement is int mp ? $"FILTER (?placement <= {mp})" : "";

        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>

SELECT ?oracleId ?name ?priceEur (COUNT(DISTINCT ?deck) AS ?deckCount) WHERE {{
  ?entry mtg:inTournament ?t ;
         mtg:hasDeck       ?deck ;
         mtg:hasPlacement  ?placement .
  ?t mtg:hasFormat ""{fmt}"" .
  ?deck mtg:containsCard ?card .
  ?card mtg:hasOracleId ?oracleId ;
        mtg:hasName     ?name .
  OPTIONAL {{ ?card mtg:hasPriceEur ?priceEur }}
  {placementFilter}
  FILTER (!BOUND(?priceEur) || ?priceEur <= ""{Fmt(maxPrice)}""^^xsd:decimal)
}}
GROUP BY ?oracleId ?name ?priceEur
HAVING (COUNT(DISTINCT ?deck) >= {minDeckCount})
ORDER BY DESC(?deckCount) ?priceEur
LIMIT {lim}";

        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        var list = new List<FormatStaple>(rs.Count);
        foreach (var row in rs)
        {
            list.Add(new FormatStaple(
                OracleId:  Str(row, "oracleId") ?? "",
                Name:      Str(row, "name") ?? "",
                PriceEur:  Dec(row, "priceEur"),
                DeckCount: (int)(Dec(row, "deckCount") ?? 0m)));
        }
        return list;
    }

    public async Task<IReadOnlyList<CommanderSummary>> ListCommandersAsync(int limit, CancellationToken ct)
    {
        var lim = Math.Clamp(limit, 1, 500);
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>
SELECT ?commander ?name ?entries ?topCuts WHERE {{
  ?commander a mtg:Commander ;
             mtg:hasName ?name .
  OPTIONAL {{ ?commander mtg:hasTournamentEntryCount ?entries }}
  OPTIONAL {{ ?commander mtg:hasTournamentTopCutCount ?topCuts }}
}}
ORDER BY DESC(?entries)
LIMIT {lim}";

        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        var list = new List<CommanderSummary>(rs.Count);
        foreach (var row in rs)
        {
            var uri = Str(row, "commander") ?? "";
            var slug = uri.StartsWith(MtgVocab.Namespace + "commander/", StringComparison.Ordinal)
                ? uri[(MtgVocab.Namespace.Length + "commander/".Length)..]
                : MtgVocab.Slugify(Str(row, "name") ?? "");
            list.Add(new CommanderSummary(
                CommanderSlug:        slug,
                Name:                 Str(row, "name") ?? "",
                TournamentEntryCount: (int)(Dec(row, "entries") ?? 0m),
                TopCutCount:          (int)(Dec(row, "topCuts") ?? 0m)));
        }
        return list;
    }

    private static string Fmt(decimal d) => d.ToString("0.############", CultureInfo.InvariantCulture);

    private static string EscapeLiteral(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string? Str(VDS.RDF.Query.ISparqlResult row, string var)
    {
        if (!row.HasBoundValue(var)) return null;
        return row[var] switch
        {
            ILiteralNode lit => lit.Value,
            IUriNode uri     => uri.Uri.AbsoluteUri,
            _                => null
        };
    }

    private static decimal? Dec(VDS.RDF.Query.ISparqlResult row, string var)
    {
        if (!row.HasBoundValue(var) || row[var] is not ILiteralNode lit) return null;
        return decimal.TryParse(lit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }
}
