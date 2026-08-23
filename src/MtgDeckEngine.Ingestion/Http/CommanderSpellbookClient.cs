using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Core.Brackets;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Ingestion.Dto;

namespace MtgDeckEngine.Ingestion.Http;

/// <summary>
/// Client for Commander Spellbook's public backend
/// (<c>https://backend.commanderspellbook.com</c>). MIT-licensed, no auth on
/// read endpoints, OpenAPI schema published at <c>/schema/</c>.
///
/// We use it for the one thing our in-engine <see cref="Core.Brackets.BracketEvaluator"/>
/// structurally cannot do: detect two-card infinite combos, which are a hard
/// Bracket-4 trigger. Spellbook maintains the combo database, so calling their
/// estimator is more accurate than reimplementing it against stale name lists.
/// </summary>
public sealed class CommanderSpellbookClient(
    IHttpClientFactory httpFactory,
    ILogger<CommanderSpellbookClient> logger)
{
    public const string HttpClientName = "CommanderSpellbook";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Estimate the Commander Bracket for a decklist. Returns null when the
    /// service is unreachable or answers with an unusable payload — callers
    /// should fall back to the local estimator rather than fail the request.
    /// </summary>
    public async Task<DeckBracket?> EstimateBracketAsync(
        string commanderName,
        IReadOnlyCollection<string> cardNames,
        CancellationToken cancellationToken = default)
    {
        var request = new SpellbookBracketRequest
        {
            Commanders = [new SpellbookCardRef { Card = commanderName, Quantity = 1 }],
            Main = cardNames
                // Basic lands carry no bracket signal and just inflate the body.
                .Where(n => !IsBasicLand(n))
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SpellbookCardRef { Card = g.Key, Quantity = g.Count() })
                .ToList(),
        };

        SpellbookBracketResponse? body;
        try
        {
            // Resolved per call rather than captured in the constructor: this
            // client is consumed by a singleton, and holding one HttpClient for
            // the app lifetime would pin a single handler past its rotation.
            using var http = httpFactory.CreateClient(HttpClientName);
            using var resp = await http
                .PostAsJsonAsync("estimate-bracket", request, Json, cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Commander Spellbook estimate-bracket returned {StatusCode}", resp.StatusCode);
                return null;
            }
            body = await resp.Content
                .ReadFromJsonAsync<SpellbookBracketResponse>(Json, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Never let an external outage break deck building — the caller
            // degrades to the local name-list estimate.
            logger.LogWarning(ex, "Commander Spellbook estimate-bracket call failed");
            return null;
        }

        if (body?.BracketTag is null) return null;
        return ToDeckBracket(body);
    }

    private static DeckBracket ToDeckBracket(SpellbookBracketResponse r)
    {
        // Spellbook's bracketTag is their own severity ladder, not WotC's
        // brackets. "Ruthless" fires on >3 Game Changers OR mass land denial OR
        // a fast combo — all of which WotC puts at Bracket 4, not 5. Taking
        // their tag as the bracket reported a 4-Game-Changer deck with no
        // combos as cEDH.
        //
        // So use them for what they uniquely know — which cards are Game
        // Changers, which pairs combo and how fast, what is banned — and apply
        // WotC's published rules here.

        var gameChangers = r.Cards
            .Where(c => c.GameChanger && c.Card?.Name is not null)
            .Select(c => c.Card!.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var banned = r.Cards
            .Where(c => c.Banned && c.Card?.Name is not null)
            .Select(c => c.Card!.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mld = r.Cards.Any(c => c.MassLandDenial);
        var extraTurns = r.Cards.Any(c => c.ExtraTurn);

        var twoCardCombos = r.Combos
            .Where(c => c.DefinitelyTwoCard && c.Relevant)
            .ToList();

        var comboParticipants = twoCardCombos
            .Select(c => (IReadOnlyList<string>)(c.Combo?.Uses
                .Select(u => u.Card?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList() ?? []))
            .Where(names => names.Count > 0)
            .ToList();

        var earlyCombos = twoCardCombos.Count(c => c.Speed >= BracketRules.EarlyComboSpeed);
        var lateCombos  = twoCardCombos.Count - earlyCombos;

        var (level, label) = BracketRules.Evaluate(new BracketRules.Signals(
            GameChangerCount:   gameChangers.Count,
            HasMassLandDenial:  mld,
            HasExtraTurns:      extraTurns,
            EarlyTwoCardCombos: earlyCombos,
            LateTwoCardCombos:  lateCombos,
            HasBannedCards:     banned.Count > 0));

        var reasons = new List<string>();
        if (banned.Count > 0)
            reasons.Add($"Banned in Commander: {string.Join(", ", banned)}.");
        if (gameChangers.Count > 0)
            reasons.Add(gameChangers.Count > 3
                ? $"{gameChangers.Count} Game Changers (more than 3 requires Bracket 4): {string.Join(", ", gameChangers)}."
                : $"{gameChangers.Count} Game Changer(s), within the 3 allowed at Bracket 3: {string.Join(", ", gameChangers)}.");
        if (twoCardCombos.Count > 0)
        {
            var samples = twoCardCombos
                .Take(3)
                .Select(c => string.Join(" + ", c.Combo?.Uses
                    .Select(u => u.Card?.Name)
                    .Where(n => n is not null) ?? []))
                .Where(s => s.Length > 0);
            reasons.Add(earlyCombos > 0
                ? $"{earlyCombos} early two-card infinite combo(s), which Bracket 3 does not allow: {string.Join("; ", samples)}."
                : $"{lateCombos} late-game two-card combo(s), permitted at Bracket 3: {string.Join("; ", samples)}.");
        }
        if (mld) reasons.Add("Contains mass land denial, which requires Bracket 4.");
        if (extraTurns) reasons.Add("Contains extra-turn effects.");
        if (reasons.Count == 0)
            reasons.Add("No Game Changers, combos, mass land denial or extra turns found.");
        if (level >= BracketRules.MaxDerivable)
            reasons.Add(BracketRules.CeilingNote);

        return new DeckBracket(
            Level:             level,
            Label:             label,
            GameChangerCount:  gameChangers.Count,
            GameChangersFound: gameChangers,
            HasMassLandDenial: mld,
            HasExtraTurns:     extraTurns,
            Reasons:           reasons,
            // Combo-aware and sourced from the maintained database, so unlike
            // the local evaluator this is not a floor-only guess.
            IsEstimate:        false,
            TwoCardCombos:     comboParticipants);
    }

    private static bool IsBasicLand(string name) => name is
        "Plains" or "Island" or "Swamp" or "Mountain" or "Forest" or "Wastes";
}
