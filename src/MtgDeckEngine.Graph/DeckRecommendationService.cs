using System.Globalization;
using System.Text;
using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Brackets;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Core.Models;
using VDS.RDF;

namespace MtgDeckEngine.Graph;

// brackets is optional so unit tests can construct the service with just a
// repository; when omitted the offline name-list estimator is used.
public sealed class DeckRecommendationService(
    IGraphRepository repo,
    IBracketService? brackets = null,
    ICommanderNameResolver? commanderNames = null) : IDeckRecommendationService
{
    private readonly IBracketService _brackets = brackets ?? new LocalBracketService();

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
            var wins   = Dec(row, "wins");
            var losses = Dec(row, "losses");
            list.Add(new CardRecommendation(
                OracleId:            Str(row, "oracleId") ?? "",
                Name:                Str(row, "name") ?? "",
                Category:            Str(row, "categoryLabel"),
                InclusionPct:        Dec(row, "inclusion"),
                SynergyScore:        Dec(row, "synergy"),
                PriceEur:            Dec(row, "priceEur"),
                TopCutAppearances:   topCut.HasValue ? (int)topCut.Value : null,
                TournamentWins:      wins.HasValue ? (int)wins.Value : null,
                TournamentLosses:    losses.HasValue ? (int)losses.Value : null,
                IsGameChanger:       Bool(row, "gameChanger"),
                ImageUrl:            Str(row, "imageUrl"),
                TypeLine:            Str(row, "typeLine"),
                ColorIdentity:       Str(row, "colorIdentity")));
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
        var entries = Dec(row, "entries");
        var topCuts = Dec(row, "topCuts");
        var winRate = Dec(row, "winRate");

        // EDHTop16 aggregates only exist for commanders we ingested from there
        // by name. Since TopDeck ingestion started identifying commanders, the
        // graph holds tournament decks for hundreds more — every one of which
        // reported a flat zero here, which reads as "never played" rather than
        // "we have no aggregate". Derive the figures from the entries we do hold.
        var derived = entries is null
            ? await GetDerivedMetaAsync(commanderUri, ct).ConfigureAwait(false)
            : null;

        var source = entries is not null ? CommanderMetaSource.EdhTop16Aggregate
                   : derived is { DeckCount: > 0 } ? CommanderMetaSource.DerivedFromEntries
                   : CommanderMetaSource.None;

        return new CommanderMeta(
            CommanderSlug:        commanderSlug,
            TournamentEntryCount: (int)(entries ?? derived?.DeckCount ?? 0m),
            TopCutCount:          (int)(topCuts ?? derived?.TopCuts ?? 0m),
            WinRate:              winRate ?? derived?.WinRate,
            ConversionRate:       Dec(row, "conversion") ?? derived?.ConversionRate,
            // Deliberately not derived: meta share is this commander's slice of
            // the whole format, and our slice of the format is not the format.
            MetaShare:            Dec(row, "metaShare"),
            Top4DeckCount:        (int)(Dec(row, "top4") ?? 0m),
            Top16DeckCount:       (int)(Dec(row, "top16") ?? 0m),
            LatestTopCutDate:     latest,
            Source:               source);
    }

    private sealed record DerivedMeta(
        int DeckCount, int TopCuts, decimal? WinRate, decimal? ConversionRate);

    /// <summary>
    /// Commander metrics computed from the tournament entries in this graph,
    /// for commanders EDHTop16 never gave us aggregates for.
    /// </summary>
    private async Task<DerivedMeta?> GetDerivedMetaAsync(string commanderUri, CancellationToken ct)
    {
        var sparql = $@"
PREFIX mtg: <{MtgVocab.Namespace}>

SELECT (COUNT(DISTINCT ?deck) AS ?deckCount)
       (SUM(?wins) AS ?w) (SUM(?losses) AS ?l) (SUM(?isTopCut) AS ?topCuts)
WHERE {{
  ?deck  mtg:hasCommander <{commanderUri}> .
  ?entry mtg:hasDeck ?deck ; mtg:hasPlacement ?placement .
  OPTIONAL {{ ?entry mtg:inTournament ?t . ?t mtg:hasTopCutSize ?cutSize }}
  OPTIONAL {{ ?entry mtg:hasWinsSwiss     ?ws }}
  OPTIONAL {{ ?entry mtg:hasWinsBracket   ?wb }}
  OPTIONAL {{ ?entry mtg:hasLossesSwiss   ?ls }}
  OPTIONAL {{ ?entry mtg:hasLossesBracket ?lb }}
  BIND (COALESCE(?ws, 0) + COALESCE(?wb, 0) AS ?wins)
  BIND (COALESCE(?ls, 0) + COALESCE(?lb, 0) AS ?losses)
  # Against the event's own cut, not a flat 16: in a 20-player tournament
  # placing 16th is not a conversion, and assuming otherwise reported a 92%
  # conversion rate for commanders that had simply attended small events.
  BIND (IF(?placement <= COALESCE(?cutSize, 16), 1, 0) AS ?isTopCut)
}}";
        var rs = await repo.QueryAsync(sparql, ct).ConfigureAwait(false);
        if (rs.Count == 0) return null;

        var row = rs.First();
        var decks = (int)(Dec(row, "deckCount") ?? 0m);
        if (decks == 0) return null;

        var wins   = Dec(row, "w") ?? 0m;
        var losses = Dec(row, "l") ?? 0m;
        var games  = wins + losses;
        var cuts   = (int)(Dec(row, "topCuts") ?? 0m);

        return new DerivedMeta(
            DeckCount:      decks,
            TopCuts:        cuts,
            WinRate:        games > 0 ? wins / games : null,
            ConversionRate: (decimal)cuts / decks);
    }

    public async Task<BudgetDeck> BuildBudgetDeckAsync(
        string commanderSlug,
        decimal totalBudgetEur,
        RecommendationFilter filter,
        CancellationToken ct)
        => await BuildBudgetDeckAsync(commanderSlug, totalBudgetEur, filter, null, ct)
            .ConfigureAwait(false);

    public async Task<BudgetDeck> BuildBudgetDeckAsync(
        string commanderSlug,
        decimal totalBudgetEur,
        RecommendationFilter filter,
        int? maxBracket,
        CancellationToken ct)
    {
        var constraint = maxBracket is int bracketTarget
            ? BracketConstraint.For(bracketTarget)
            : null;
        // Fetch a wide pool WITHOUT the EDHREC-inclusion requirement so
        // tournament-only staples and unpriced cards still surface.
        var poolFilter = filter with
        {
            Limit = 500,
            RequireInclusion = false,
            ExcludeBasicLands = false,
        };
        var raw = await GetRecommendationsAsync(commanderSlug, poolFilter, ct);

        // De-dupe by oracle id and drop any land-less/nameless junk. Nonbasic
        // lands from the graph stay in the pool; basic lands are synthesised.
        var pool = raw
            .Where(c => !string.IsNullOrEmpty(c.OracleId) && !string.IsNullOrEmpty(c.Name))
            .GroupBy(c => c.OracleId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Where(c => !IsBasicLand(c))
            // A card with no price in the graph cannot be budgeted. Treating it
            // as EUR 0 (the old `PriceEur ?? 0m` behaviour) let unpriced staples
            // like Force of Will and Tropical Island enter a "EUR 100" deck for
            // free, so the reported total bore no relation to what the deck costs.
            // Same semantic the recommendations budget filter already uses.
            .Where(c => c.PriceEur is not null)
            // Cards the target bracket forbids outright never enter the pool, so
            // neither the skeleton nor the upgrade pass can pick them up.
            .Where(c => constraint is null || !IsBannedByBracket(constraint, c))
            .ToList();

        var score = BuildScoreMap(pool);
        var identity = DeckColorIdentity(pool);
        var quotas = DeckBuildQuotas.Default;

        var lands    = pool.Where(IsLand).ToList();
        var nonlands = pool.Where(c => !IsLand(c)).ToList();

        // ---- Phase A — complete a legal deck as cheaply as possible ----
        // Manabase is all basics (€0); nonland slots take the cheapest cards per
        // role so the deck reaches ~99 for essentially no cost. Phase B then
        // spends the budget upgrading these fillers to the best cards that fit.
        var chosen = new List<CardRecommendation>(99);
        var inDeck = new HashSet<string>(StringComparer.Ordinal);
        var running = 0m;

        // Nonland skeleton: cheapest cards, honouring soft per-role targets, then
        // a second sweep to guarantee the 62-card nonland count.
        var nonlandTarget = quotas.Sum - quotas.Lands; // 62
        var bucketCount = new Dictionary<DeckBucket, int>();
        foreach (var card in nonlands.OrderBy(c => c.PriceEur ?? 0m))
        {
            if (chosen.Count >= nonlandTarget) break;
            var bucket = NonlandBucket(card);
            var target = QuotaFor(quotas, bucket);
            if (bucketCount.GetValueOrDefault(bucket) >= target) continue;
            if (!TryTake(card, totalBudgetEur, chosen, inDeck, ref running)) continue;
            bucketCount[bucket] = bucketCount.GetValueOrDefault(bucket) + 1;
        }
        foreach (var card in nonlands.OrderBy(c => c.PriceEur ?? 0m))
        {
            if (chosen.Count >= nonlandTarget) break;
            TryTake(card, totalBudgetEur, chosen, inDeck, ref running);
        }

        // Manabase: fill the land quota entirely with basics (free) for now.
        var basics = BuildBasics(identity, quotas.Lands);
        chosen.AddRange(basics);

        // ---- Phase B — upgrade within budget ----
        // Repeatedly apply the single most cost-efficient improving swap (replace
        // an in-deck card with a higher-score pool card of the same slot type)
        // while staying within budget. Never drops below 99, never exceeds budget.
        UpgradeWithinBudget(
            chosen, inDeck, lands, nonlands, score, totalBudgetEur, constraint, ref running);

        // ---- Phase C — bring the deck under the bracket cap ----
        var bracket = await EnforceBracketCapAsync(
            commanderSlug, chosen, inDeck, lands, nonlands, score,
            totalBudgetEur, constraint, maxBracket, ct).ConfigureAwait(false);

        var total = chosen.Sum(c => c.PriceEur ?? 0m);
        var commanderName = commanderNames is null
            ? null
            : await commanderNames.ResolveAsync(commanderSlug, ct).ConfigureAwait(false);
        return new BudgetDeck(
            commanderSlug, total, chosen.Count, chosen, bracket, commanderName);
    }

    /// <summary>
    /// Evaluate the bracket and, if it overshoots the cap because of two-card
    /// infinite combos, break them and try again.
    ///
    /// Combos are the one bracket trigger the builder cannot see while building:
    /// they are a property of card *pairs*, not of any card's flags, and only
    /// Commander Spellbook knows them. So the Game Changer cap could hold
    /// perfectly and the deck still come back cEDH — which it did for Kefka,
    /// whose commander combos with Psychosis Crawler.
    ///
    /// Checking each candidate during the greedy upgrade would mean a network
    /// call per candidate, hundreds per build. Repairing afterwards costs one
    /// extra call per round instead, and converges in one or two.
    /// </summary>
    private async Task<DeckBracket> EnforceBracketCapAsync(
        string commanderSlug,
        List<CardRecommendation> chosen,
        HashSet<string> inDeck,
        List<CardRecommendation> lands,
        List<CardRecommendation> nonlands,
        IReadOnlyDictionary<string, double> score,
        decimal budget,
        BracketConstraint? constraint,
        int? maxBracket,
        CancellationToken ct)
    {
        const int maxRepairRounds = 3;

        var bracket = await _brackets
            .EvaluateAsync(commanderSlug, chosen.Select(c => c.Name).ToList(), ct)
            .ConfigureAwait(false);

        // Brackets 4 and 5 permit combos, so there is nothing to repair.
        if (maxBracket is not int cap || cap >= 4) return bracket;

        var banned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var round = 0; round < maxRepairRounds; round++)
        {
            if (bracket.Level <= cap) break;
            if (bracket.TwoCardCombos is not { Count: > 0 }) break;   // over cap for some other reason

            // One cut can break several combos at once when they share a card,
            // so choose the smallest set that hits them all rather than cutting
            // each combo's weakest half independently. Every avoided cut is a
            // card the budget paid for and the scorer wanted.
            var inDeckByName = chosen
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var toCut = ComboBreaker.ChooseCardsToCut(
                bracket.TwoCardCombos,
                isRemovable: name => inDeckByName.ContainsKey(name),
                scoreOf:     name => inDeckByName.TryGetValue(name, out var c)
                                 ? ScoreOf(score, c) : double.MaxValue);

            // Nothing we could remove — every combo runs through the commander,
            // which is not one of the 99. Report the real bracket rather than looping.
            if (toCut.Count == 0) break;

            foreach (var name in toCut)
            {
                if (!inDeckByName.TryGetValue(name, out var victim)) continue;
                banned.Add(victim.Name);
                chosen.Remove(victim);
                inDeck.Remove(victim.OracleId);
            }

            RefillAndUpgrade(chosen, inDeck, lands, nonlands, score, budget, constraint, banned);

            bracket = await _brackets
                .EvaluateAsync(commanderSlug, chosen.Select(c => c.Name).ToList(), ct)
                .ConfigureAwait(false);
        }

        return bracket;
    }

    /// <summary>
    /// Top the deck back up to 99 after cards were cut, then re-run the upgrade
    /// pass. <paramref name="banned"/> holds names that must not come back —
    /// without it the upgrade would immediately re-pick the combo piece it just
    /// removed, since that is exactly the card it rates highest.
    /// </summary>
    private static void RefillAndUpgrade(
        List<CardRecommendation> chosen,
        HashSet<string> inDeck,
        List<CardRecommendation> lands,
        List<CardRecommendation> nonlands,
        IReadOnlyDictionary<string, double> score,
        decimal budget,
        BracketConstraint? constraint,
        IReadOnlySet<string> banned)
    {
        var quotas = DeckBuildQuotas.Default;
        var running = chosen.Sum(c => c.PriceEur ?? 0m);

        foreach (var card in nonlands
                     .Where(c => !banned.Contains(c.Name))
                     .OrderBy(c => c.PriceEur ?? 0m))
        {
            if (chosen.Count >= quotas.Sum) break;
            if (constraint is not null && IsBannedByBracket(constraint, card)) continue;
            TryTake(card, budget, chosen, inDeck, ref running);
        }

        UpgradeWithinBudget(
            chosen, inDeck, lands,
            nonlands.Where(c => !banned.Contains(c.Name)).ToList(),
            score, budget, constraint, ref running);
    }

    private static bool TryTake(
        CardRecommendation card, decimal budget,
        List<CardRecommendation> chosen, HashSet<string> inDeck, ref decimal running)
    {
        if (!inDeck.Add(card.OracleId)) return false;
        var price = card.PriceEur ?? 0m;
        if (running + price > budget) { inDeck.Remove(card.OracleId); return false; }
        chosen.Add(card);
        running += price;
        return true;
    }

    // Greedy knapsack-style upgrade: each round pick the affordable improving
    // swap with the best score-gain-per-euro and apply it. Lands upgrade to
    // nonbasic lands from the pool; nonland cards upgrade within their role.
    private static void UpgradeWithinBudget(
        List<CardRecommendation> chosen, HashSet<string> inDeck,
        List<CardRecommendation> lands, List<CardRecommendation> nonlands,
        IReadOnlyDictionary<string, double> score, decimal budget,
        BracketConstraint? constraint, ref decimal running)
    {
        const int maxRounds = 400;
        // Game Changers are capped per bracket rather than banned, so the count
        // has to be tracked as the deck changes. (Cards the bracket bans outright
        // were already dropped from the pool.)
        var gameChangers = constraint is null
            ? 0
            : chosen.Count(c => IsGameChanger(constraint, c));
        for (var round = 0; round < maxRounds; round++)
        {
            int bestOut = -1;
            CardRecommendation? bestIn = null;
            var bestValue = 0.0;

            for (var i = 0; i < chosen.Count; i++)
            {
                var outCard = chosen[i];
                var outScore = ScoreOf(score, outCard);
                var candidates = IsLand(outCard) ? lands : NonlandsInRole(nonlands, outCard);
                foreach (var inCard in candidates)
                {
                    if (inDeck.Contains(inCard.OracleId)) continue;
                    // Respect the Game Changer budget: swapping a plain card for
                    // a Game Changer is only allowed while under the cap.
                    if (constraint is not null
                        && IsGameChanger(constraint, inCard)
                        && !IsGameChanger(constraint, outCard)
                        && gameChangers >= constraint.MaxGameChangers) continue;
                    var gain = ScoreOf(score, inCard) - outScore;
                    if (gain <= 0) continue;
                    var deltaCost = (inCard.PriceEur ?? 0m) - (outCard.PriceEur ?? 0m);
                    if (running + deltaCost > budget) continue;
                    // Prefer free/cheap gains: value = score gain per euro spent
                    // (deltaCost ≤ 0 is treated as maximally efficient).
                    var value = deltaCost <= 0 ? gain * 1_000_000 : gain / (double)deltaCost;
                    if (value > bestValue)
                    {
                        bestValue = value;
                        bestOut = i;
                        bestIn = inCard;
                    }
                }
            }

            if (bestIn is null || bestOut < 0) break;
            var replaced = chosen[bestOut];
            if (constraint is not null)
            {
                if (IsGameChanger(constraint, bestIn)) gameChangers++;
                if (IsGameChanger(constraint, replaced)) gameChangers--;
            }
            inDeck.Remove(replaced.OracleId);
            inDeck.Add(bestIn.OracleId);
            running += (bestIn.PriceEur ?? 0m) - (replaced.PriceEur ?? 0m);
            chosen[bestOut] = bestIn;
        }
    }

    // Prefer Scryfall's per-card flag — it tracks WotC's list automatically.
    // The curated name list in BracketEvaluator only backstops cards ingested
    // before the flag existed in the graph.
    private static bool IsGameChanger(BracketConstraint c, CardRecommendation card)
        => card.IsGameChanger || c.IsGameChanger(card.Name);

    private static bool IsBannedByBracket(BracketConstraint c, CardRecommendation card)
        => (c.MaxGameChangers == 0 && IsGameChanger(c, card)) || c.IsBanned(card.Name);

    private static IEnumerable<CardRecommendation> NonlandsInRole(
        List<CardRecommendation> nonlands, CardRecommendation outCard)
    {
        var role = NonlandBucket(outCard);
        return nonlands.Where(c => NonlandBucket(c) == role);
    }

    private static double ScoreOf(IReadOnlyDictionary<string, double> score, CardRecommendation c)
        => score.TryGetValue(c.OracleId, out var s) ? s : 0.0;

    // How strongly a card's observed record is pulled toward the pool average.
    // In units of games: a card with PriorGames games of evidence sits halfway
    // between its own record and the pool mean. Commander tournament samples are
    // small (a card in 3 decks might be 9-2 by luck), so this keeps low-evidence
    // cards from topping the ranking.
    private const double PriorGames = 20.0;

    /// <summary>
    /// Per-card score used to rank upgrades, 0–1. The dominant term is the
    /// shrunk tournament win rate of the decks that played the card; top-cut
    /// volume, EDHREC inclusion and synergy fill in where match records are
    /// thin or absent.
    /// </summary>
    private static Dictionary<string, double> BuildScoreMap(IReadOnlyList<CardRecommendation> pool)
    {
        // Pool-wide prior: the aggregate win rate across every card with a
        // record. Falls back to 0.5 when the graph has no tournament data.
        double totalWins = 0, totalGames = 0;
        foreach (var c in pool)
        {
            totalWins  += c.TournamentWins ?? 0;
            totalGames += c.TournamentGames ?? 0;
        }
        var poolMean = totalGames > 0 ? totalWins / totalGames : 0.5;

        double maxTop = 0, maxIncl = 0, maxSyn = 0;
        foreach (var c in pool)
        {
            maxTop  = Math.Max(maxTop,  c.TopCutAppearances ?? 0);
            maxIncl = Math.Max(maxIncl, (double)(c.InclusionPct ?? 0m));
            maxSyn  = Math.Max(maxSyn,  Math.Max(0, (double)(c.SynergyScore ?? 0m)));
        }

        var map = new Dictionary<string, double>(pool.Count, StringComparer.Ordinal);
        foreach (var c in pool)
        {
            var games = (double)(c.TournamentGames ?? 0);
            var wins  = (double)(c.TournamentWins ?? 0);

            // Bayesian shrinkage toward the pool mean. With no games this is
            // exactly poolMean, so a card with no record is neither rewarded
            // nor punished — it just leans on the other signals below.
            var shrunk = (wins + PriorGames * poolMean) / (games + PriorGames);

            // Re-centre on the pool mean and rescale to 0–1 so that "average
            // win rate" sits at 0.5 and the spread actually separates cards.
            // Without this every card clusters near poolMean and the term
            // stops discriminating.
            var winTerm = Math.Clamp(0.5 + (shrunk - poolMean) * 2.0, 0.0, 1.0);

            // Confidence in that term: 0 with no games, approaching 1 as the
            // sample grows past the prior. Low-evidence cards therefore fall
            // back to popularity rather than to a noisy win rate.
            var confidence = games / (games + PriorGames);

            var top  = maxTop  > 0 ? (c.TopCutAppearances ?? 0) / maxTop : 0;
            var incl = maxIncl > 0 ? (double)(c.InclusionPct ?? 0m) / maxIncl : 0;
            var syn  = maxSyn  > 0 ? Math.Max(0, (double)(c.SynergyScore ?? 0m)) / maxSyn : 0;

            // The win term claims weight in proportion to its confidence; the
            // rest of that weight reverts to the popularity signals.
            var evidenceWeight = 0.50 * confidence;
            var popularityWeight = 0.50 - evidenceWeight;

            map[c.OracleId] =
                  evidenceWeight   * winTerm
                + popularityWeight * top
                + 0.30 * top
                + 0.15 * incl
                + 0.05 * syn;
        }
        return map;
    }

    private static HashSet<char> DeckColorIdentity(IReadOnlyList<CardRecommendation> pool)
    {
        var colors = new HashSet<char>();
        foreach (var c in pool)
        {
            if (string.IsNullOrEmpty(c.ColorIdentity)) continue;
            foreach (var part in c.ColorIdentity.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (part.Length == 1 && "WUBRG".Contains(part[0]))
                    colors.Add(part[0]);
        }
        return colors;
    }

    // Synthesise the basic-land manabase in the deck's colour identity. Colourless
    // commanders get Wastes. Copies are distributed as evenly as possible.
    private static List<CardRecommendation> BuildBasics(HashSet<char> identity, int count)
    {
        var list = new List<CardRecommendation>(count);
        var order = new[] { 'W', 'U', 'B', 'R', 'G' }.Where(identity.Contains).ToList();
        if (order.Count == 0) order.Add('C'); // colourless → Wastes
        for (var i = 0; i < count; i++)
        {
            var code = order[i % order.Count];
            var name = BasicLandName(code);
            list.Add(new CardRecommendation(
                OracleId:      $"basic-{name.ToLowerInvariant()}-{i}",
                Name:          name,
                Category:      "Lands",
                InclusionPct:  null,
                SynergyScore:  null,
                PriceEur:      0m,
                TypeLine:      $"Basic Land — {name}"));
        }
        return list;
    }

    private static string BasicLandName(char code) => code switch
    {
        'W' => "Plains",
        'U' => "Island",
        'B' => "Swamp",
        'R' => "Mountain",
        'G' => "Forest",
        _   => "Wastes",
    };

    private static bool IsLand(CardRecommendation c)
        => (c.TypeLine ?? "").Contains("Land", StringComparison.OrdinalIgnoreCase)
        || (c.Category ?? "").Contains("land", StringComparison.OrdinalIgnoreCase);

    private static bool IsBasicLand(CardRecommendation c)
        => (c.TypeLine ?? "").Contains("Basic", StringComparison.OrdinalIgnoreCase);

    // Roles the quota model cares about, for nonland cards. EDHREC's free-form
    // labels ("Top Cards" / "High Synergy") collapse to "Other".
    private enum DeckBucket { Lands, Ramp, Draw, Removal, Creatures, Other }

    private static DeckBucket NonlandBucket(CardRecommendation card)
    {
        var c = (card.Category ?? "").ToLowerInvariant();
        if (c.Contains("ramp")) return DeckBucket.Ramp;
        if (c.Contains("card draw") || c == "draw") return DeckBucket.Draw;
        if (c.Contains("removal") || c.Contains("interaction")) return DeckBucket.Removal;
        var t = (card.TypeLine ?? "").ToLowerInvariant();
        if (c.Contains("creature") || t.Contains("creature")) return DeckBucket.Creatures;
        return DeckBucket.Other;
    }

    private static int QuotaFor(DeckBuildQuotas q, DeckBucket b) => b switch
    {
        DeckBucket.Lands     => q.Lands,
        DeckBucket.Ramp      => q.Ramp,
        DeckBucket.Draw      => q.Draw,
        DeckBucket.Removal   => q.Removal,
        DeckBucket.Creatures => q.Creatures,
        _                    => q.Other,
    };

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
        // When inclusion is optional the EDHREC context block is wrapped in
        // OPTIONAL and the inclusion floor filter is skipped — used by the
        // budget builder so tournament-only / unpriced cards still surface.
        var inclusionOptional = tourMode || !f.RequireInclusion;

        var sb = new StringBuilder();
        sb.AppendLine($"PREFIX mtg:  <{MtgVocab.Namespace}>");
        sb.AppendLine("PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>");
        sb.AppendLine("PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>");
        sb.AppendLine();
        sb.AppendLine("SELECT ?oracleId ?name ?categoryLabel ?inclusion ?synergy ?priceEur ?topCutCount ?wins ?losses ?imageUrl ?typeLine ?colorIdentity ?gameChanger WHERE {");

        // EDHREC context graph — required for Edhrec/All sources, optional when
        // inclusion is not required (Tournament source or the budget builder).
        if (inclusionOptional) sb.AppendLine("  OPTIONAL {");
        sb.AppendLine($"  GRAPH <{ctx}> {{");
        sb.AppendLine("    ?card mtg:hasInclusionPct ?inclusion .");
        sb.AppendLine("    OPTIONAL { ?card mtg:hasSynergyScore ?synergy }");
        sb.AppendLine("  }");
        if (inclusionOptional) sb.AppendLine("  }");

        sb.AppendLine("  ?card mtg:hasOracleId ?oracleId ;");
        sb.AppendLine("        mtg:hasName     ?name ;");
        sb.AppendLine("        mtg:hasTypeLine ?typeLine .");
        // Price and image can each have SEVERAL triples per card (repeated
        // ingestion over time / multiple printings). Fold them to one value per
        // card with grouped subqueries — inline OPTIONALs would cross-multiply
        // the outer rows so LIMIT keeps only a handful of distinct cards.
        // MIN price = the cheapest printing, which is the right budget semantic.
        // Many cards have no EUR price (reserved-list / older singles); fall back
        // to USD (EUR≈USD for most ranges) so budget queries still work.
        sb.AppendLine("  OPTIONAL { SELECT ?card (MIN(?e) AS ?eurMin) WHERE { ?card mtg:hasPriceEur ?e } GROUP BY ?card }");
        sb.AppendLine("  OPTIONAL { SELECT ?card (MIN(?u) AS ?usdMin) WHERE { ?card mtg:hasPriceUsd ?u } GROUP BY ?card }");
        sb.AppendLine("  BIND (COALESCE(?eurMin, ?usdMin) AS ?priceEur)");
        sb.AppendLine("  OPTIONAL { SELECT ?card (SAMPLE(?img) AS ?imageUrl) WHERE { ?card mtg:hasImageUrl ?img } GROUP BY ?card }");
        sb.AppendLine("  OPTIONAL { ?card mtg:isGameChanger ?gameChanger }");

        // Colour identity as a comma-joined WUBRG string (e.g. "R,G,U"). Done as
        // a grouped subquery so multiple hasColorIdentity triples collapse into
        // one value instead of multiplying the outer rows.
        sb.AppendLine("  OPTIONAL {");
        sb.AppendLine("    SELECT ?card (GROUP_CONCAT(DISTINCT ?ci; SEPARATOR=\",\") AS ?colorIdentity) WHERE {");
        sb.AppendLine("      ?card mtg:hasColorIdentity ?ciNode .");
        sb.AppendLine("      BIND (STRAFTER(STR(?ciNode), \"color/\") AS ?ci)");
        sb.AppendLine("    } GROUP BY ?card");
        sb.AppendLine("  }");

        // EDHREC category labels, comma-joined per card. Also a grouped subquery:
        // a card sits in several categories, and joining inCategory inline would
        // multiply the outer rows (so LIMIT would keep only a few distinct cards).
        sb.AppendLine("  OPTIONAL {");
        sb.AppendLine($"    SELECT ?card (GROUP_CONCAT(DISTINCT ?lbl; SEPARATOR=\",\") AS ?categoryLabel) WHERE {{");
        sb.AppendLine($"      GRAPH <{ctx}> {{ ?card mtg:inCategory ?cat . OPTIONAL {{ ?cat rdfs:label ?lbl }} }}");
        sb.AppendLine("    } GROUP BY ?card");
        sb.AppendLine("  }");

        // Tournament subquery — per card, across this commander's tournament
        // decks: how many distinct decks played it, and the aggregate match
        // record of those decks. Deck <-> entry is 1:1 (the deck URI is keyed by
        // tournament + entry), so summing wins over rows sums them over entries.
        //
        // This is the actual win signal: EDHTop16 gives a Swiss and a bracket
        // record per entry, which the ingestion mapper already asserts. Counting
        // top-cut appearances alone answers "how often is this card played by
        // good decks", not "how often do decks playing it win".
        if (includeTournamentCount)
        {
            var wrap = tourMode ? "" : "OPTIONAL ";
            sb.AppendLine($"  {wrap}{{");
            sb.AppendLine("    SELECT ?card (COUNT(DISTINCT ?deck) AS ?topCutCount)");
            sb.AppendLine("                 (SUM(?entryWins) AS ?wins) (SUM(?entryLosses) AS ?losses) WHERE {");
            sb.AppendLine("      ?entry mtg:hasPlacement ?p ; mtg:hasDeck ?deck .");
            sb.AppendLine($"      ?deck  mtg:hasCommander <{commander}> ; mtg:containsCard ?card .");
            sb.AppendLine("      OPTIONAL { ?entry mtg:hasWinsSwiss     ?ws }");
            sb.AppendLine("      OPTIONAL { ?entry mtg:hasWinsBracket   ?wb }");
            sb.AppendLine("      OPTIONAL { ?entry mtg:hasLossesSwiss   ?ls }");
            sb.AppendLine("      OPTIONAL { ?entry mtg:hasLossesBracket ?lb }");
            sb.AppendLine("      BIND (COALESCE(?ws, 0) + COALESCE(?wb, 0) AS ?entryWins)");
            sb.AppendLine("      BIND (COALESCE(?ls, 0) + COALESCE(?lb, 0) AS ?entryLosses)");
            if (f.MaxPlacement is not null)
                sb.AppendLine($"      FILTER (?p <= {f.MaxPlacement.Value})");
            sb.AppendLine("    } GROUP BY ?card");
            sb.AppendLine("  }");
        }

        // Filters.
        // Budget filter semantics: when the caller sets MaxPriceEur, treat
        // unknown-price cards as "potentially expensive" and exclude them.
        // Pre-fix bug: "Best under €5" was including ~€60 cards whose EUR
        // price wasn't in Scryfall's data (BOUND was false → filter passed).
        // The COALESCE(eur, usd) BIND above already turns "USD-only" cards
        // into priced rows, so the remaining unbound cases really are
        // unknown — drop them from explicit budget queries.
        if (f.MaxPriceEur is not null)
            sb.AppendLine($"  FILTER (BOUND(?priceEur) && ?priceEur <= \"{Fmt(maxPrice)}\"^^xsd:decimal)");
        if (!inclusionOptional)
            sb.AppendLine($"  FILTER (BOUND(?inclusion) && ?inclusion >= \"{Fmt(minIncl)}\"^^xsd:decimal)");
        // When inclusion isn't required, still keep the pool commander-relevant:
        // a card must be in this commander's EDHREC context OR appear in one of
        // its tournament decks — otherwise the pool is the entire card database.
        else
            sb.AppendLine("  FILTER (BOUND(?inclusion) || BOUND(?topCutCount))");
        if (f.MinSynergy is not null)
            sb.AppendLine($"  FILTER (BOUND(?synergy) && ?synergy >= \"{Fmt(minSyn)}\"^^xsd:decimal)");
        if (f.ExcludeLands)
            sb.AppendLine("  FILTER (!CONTAINS(?typeLine, \"Land\"))");
        if (f.ExcludeBasicLands)
            sb.AppendLine("  FILTER (!CONTAINS(?typeLine, \"Basic Land\"))");
        // categoryLabel is now a comma-joined string (a card spans several
        // categories), so match with CONTAINS rather than IN.
        if (f.ExcludeCategories is { Count: > 0 })
        {
            var blocked = string.Join(" && ",
                f.ExcludeCategories.Select(c => $"!CONTAINS(?categoryLabel, \"{c}\")"));
            sb.AppendLine($"  FILTER (!BOUND(?categoryLabel) || ({blocked}))");
        }
        if (f.IncludeOnlyCategories is { Count: > 0 })
        {
            var allowed = string.Join(" || ",
                f.IncludeOnlyCategories.Select(c => $"CONTAINS(?categoryLabel, \"{c}\")"));
            sb.AppendLine($"  FILTER (BOUND(?categoryLabel) && ({allowed}))");
        }
        if (f.MinTopCutAppearances is not null)
            sb.AppendLine($"  FILTER (BOUND(?topCutCount) && ?topCutCount >= {f.MinTopCutAppearances.Value})");
        sb.AppendLine("}");

        sb.AppendLine(tourMode
            ? "ORDER BY DESC(?wins) DESC(?topCutCount) DESC(?inclusion)"
            : allMode
                ? "ORDER BY DESC(?topCutCount) DESC(?inclusion) DESC(?synergy)"
                : "ORDER BY DESC(?inclusion) DESC(?synergy)");
        sb.AppendLine($"LIMIT {lim}");
        return sb.ToString();
    }

    private static bool Bool(VDS.RDF.Query.ISparqlResult row, string var)
        => row.HasBoundValue(var) && row[var] is ILiteralNode lit
        && bool.TryParse(lit.Value, out var b) && b;

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
