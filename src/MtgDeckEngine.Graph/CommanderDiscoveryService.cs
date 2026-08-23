using System.Globalization;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Brackets;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using VDS.RDF;

namespace MtgDeckEngine.Graph;

/// <summary>
/// Answers "which commanders should I build for bracket X within budget Y?".
///
/// Reads deck-level rollups (<c>hasTotalPriceEur</c>, <c>hasGameChangerCount</c>)
/// that ingestion precomputes. Deriving those here would mean summing ~100 card
/// prices per deck across tens of thousands of decks on every request.
/// </summary>
public sealed class CommanderDiscoveryService(IGraphRepository repo) : ICommanderDiscoveryService
{
    // 95% confidence (1.96 sigma) for the Wilson lower bound below.
    private const double Z = 1.96;

    public async Task<IReadOnlyList<CommanderPick>> FindCommandersAsync(
        CommanderDiscoveryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var sparql = BuildQuery(filter);
        var rs = await repo.QueryAsync(sparql, cancellationToken).ConfigureAwait(false);

        var rows = rs.ToList();
        var picks = new List<CommanderPick>(rows.Count);
        foreach (var row in rows)
        {
            var wins   = (int)(Dec(row, "wins") ?? 0m);
            var losses = (int)(Dec(row, "losses") ?? 0m);
            var games  = wins + losses;
            var gc     = (int)(Dec(row, "maxGameChangers") ?? 0m);

            var adjusted = WilsonLowerBound(wins, games);

            picks.Add(new CommanderPick(
                CommanderSlug:      SlugKey(row),
                Name:               Str(row, "name") ?? "",
                DeckCount:          (int)(Dec(row, "deckCount") ?? 0m),
                TournamentWins:     wins,
                TournamentLosses:   losses,
                WinRate:            games > 0 ? (decimal)wins / games : null,
                AdjustedWinRate:    (decimal)adjusted,
                TopCutCount:        (int)(Dec(row, "topCuts") ?? 0m),
                MinDeckPriceEur:    Dec(row, "minPrice"),
                AvgDeckPriceEur:    Dec(row, "avgPrice"),
                MaxGameChangers:    gc,
                EstimatedBracket:   BracketFloorFor(gc),
                ImageUrl:           Str(row, "imageUrl")));
        }

        return picks
            .OrderByDescending(p => p.AdjustedWinRate)
            .ThenByDescending(p => p.DeckCount)
            .Take(Math.Clamp(filter.Limit, 1, 200))
            .ToList();
    }

    /// <summary>
    /// Lower bound of the Wilson score interval — the win rate we can be 95%
    /// confident the commander is at least achieving.
    ///
    /// Shrinking toward the pool average was tried first and does not work for
    /// ranking: it keeps <i>any</i> above-average commander above an average one
    /// regardless of sample size, so a single 3-0 deck still topped a commander
    /// with thirty events. Wilson penalises the interval width instead, so that
    /// 3-0 lands near 0.44 while a 210-90 record holds around 0.65.
    /// </summary>
    internal static decimal WilsonLowerBound(int wins, int games)
    {
        if (games <= 0) return 0m;
        var n = (double)games;
        var p = wins / n;
        var z2 = Z * Z;
        var centre = p + z2 / (2 * n);
        var margin = Z * Math.Sqrt((p * (1 - p) + z2 / (4 * n)) / n);
        var bound = (centre - margin) / (1 + z2 / n);
        return (decimal)Math.Clamp(bound, 0.0, 1.0);
    }

    /// <summary>
    /// Bracket floor implied by a Game Changer count, per WotC's rules: none is
    /// Core, one to three is Upgraded, more than three is Optimized. A floor,
    /// not a verdict — two-card combos are invisible from card flags alone.
    /// </summary>
    public static int BracketFloorFor(int gameChangers) => gameChangers switch
    {
        0        => 2,
        <= 3     => 3,
        _        => 4,
    };

    private static string BuildQuery(CommanderDiscoveryFilter f)
    {
        // Bracket cap becomes a cap on Game Changers per deck, which is the part
        // of the bracket rules that is checkable from the graph.
        var gcCap = f.MaxBracket switch
        {
            1 or 2 => 0,
            3      => 3,
            _      => (int?)null,
        };

        var deckFilters = "";
        if (gcCap is int cap)
            deckFilters += $"      FILTER (?gc <= {cap})\n";
        if (f.MaxBudgetEur is decimal budget)
            deckFilters += $"      FILTER (BOUND(?price) && ?price <= \"{Fmt(budget)}\"^^xsd:decimal)\n";

        return $@"
PREFIX mtg: <{MtgVocab.Namespace}>
PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>

SELECT ?cmdKey (SAMPLE(?cmdNames) AS ?name)
       (COUNT(DISTINCT ?deck) AS ?deckCount)
       (SUM(?deckWins)   AS ?wins)
       (SUM(?deckLosses) AS ?losses)
       (SUM(?isTopCut)   AS ?topCuts)
       (MIN(?price)      AS ?minPrice)
       (AVG(?price)      AS ?avgPrice)
       (MAX(?gc)         AS ?maxGameChangers)
       (SAMPLE(?img)     AS ?imageUrl)
WHERE {{
  # A third of EDH decks run a partner or background pair, so the unit of
  # interest is the command zone, not one card: Thrasios on his own is not a
  # deck, and crediting the pair's record to each half twice over would rank
  # partner halves above real single commanders.
  #
  # The key is MIN|MAX of the slugs rather than a GROUP_CONCAT, so it does not
  # depend on the order the store happens to return them in — otherwise the
  # same pair splits into ""a+b"" and ""b+a"".
  {{
    SELECT ?deck
           (CONCAT(MIN(?slug), ""|"", MAX(?slug)) AS ?cmdKey)
           (GROUP_CONCAT(DISTINCT ?cname; SEPARATOR="" + "") AS ?cmdNames)
           (SAMPLE(?cimg) AS ?img)
    WHERE {{
      ?deck mtg:hasCommander ?commander .
      ?commander mtg:hasName ?cname .
      BIND (STRAFTER(STR(?commander), ""commander/"") AS ?slug)
      OPTIONAL {{ ?commander mtg:isCardOf ?cmdCard . ?cmdCard mtg:hasImageUrl ?cimg }}
    }}
    GROUP BY ?deck
  }}

  ?deck mtg:hasGameChangerCount ?gc .
  OPTIONAL {{ ?deck mtg:hasTotalPriceEur ?price }}

  # One entry per deck, so summing over rows sums over entries.
  ?entry mtg:hasDeck ?deck ; mtg:hasPlacement ?placement .
  OPTIONAL {{ ?entry mtg:hasWinsSwiss     ?ws }}
  OPTIONAL {{ ?entry mtg:hasWinsBracket   ?wb }}
  OPTIONAL {{ ?entry mtg:hasLossesSwiss   ?ls }}
  OPTIONAL {{ ?entry mtg:hasLossesBracket ?lb }}
  BIND (COALESCE(?ws, 0) + COALESCE(?wb, 0) AS ?deckWins)
  BIND (COALESCE(?ls, 0) + COALESCE(?lb, 0) AS ?deckLosses)
  BIND (IF(?placement <= 16, 1, 0) AS ?isTopCut)

{deckFilters}}}
GROUP BY ?cmdKey
HAVING (COUNT(DISTINCT ?deck) >= {Math.Max(1, f.MinDeckCount)})
ORDER BY DESC(SUM(?deckWins))
LIMIT 400";
    }

    private static string Fmt(decimal d) => RdfLiterals.Decimal(d);

    /// <summary>
    /// The command zone as a slug. A single commander collapses "a|a" back to
    /// "a" so it stays usable as a URL segment for the other endpoints; a
    /// partner pair keeps both halves joined by "+".
    /// </summary>
    private static string SlugKey(VDS.RDF.Query.ISparqlResult row)
    {
        var key = Str(row, "cmdKey") ?? "";
        var parts = key.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == parts[1]
            ? parts[0]
            : string.Join('+', parts);
    }

    private static string? Str(VDS.RDF.Query.ISparqlResult row, string var)
        => row.HasBoundValue(var) && row[var] is ILiteralNode lit ? lit.Value : null;

    private static decimal? Dec(VDS.RDF.Query.ISparqlResult row, string var)
    {
        if (!row.HasBoundValue(var) || row[var] is not ILiteralNode lit) return null;
        return decimal.TryParse(lit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }
}
