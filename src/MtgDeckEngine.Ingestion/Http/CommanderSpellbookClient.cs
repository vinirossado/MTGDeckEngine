using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
        var (level, label) = MapTag(r.BracketTag!);

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

        var reasons = new List<string> { $"Commander Spellbook bracket tag '{r.BracketTag}' → {label}." };
        if (banned.Count > 0)
            reasons.Add($"Banned in Commander: {string.Join(", ", banned)}.");
        if (gameChangers.Count > 0)
            reasons.Add($"{gameChangers.Count} Game Changer(s): {string.Join(", ", gameChangers)}.");
        if (twoCardCombos.Count > 0)
        {
            var samples = twoCardCombos
                .Take(3)
                .Select(c => string.Join(" + ", c.Combo?.Uses
                    .Select(u => u.Card?.Name)
                    .Where(n => n is not null) ?? []))
                .Where(s => s.Length > 0);
            reasons.Add(
                $"{twoCardCombos.Count} two-card infinite combo(s) detected: {string.Join("; ", samples)}.");
        }
        if (mld) reasons.Add("Contains mass land denial.");
        if (extraTurns) reasons.Add("Contains extra-turn effects.");

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

    /// <summary>
    /// Spellbook grades on seven tags; WotC's published system has five brackets.
    /// 'Oddball' sits between brackets 2 and 3 — we round it down to 2 so a deck
    /// is never advertised as weaker-bracket-legal than it is. 'Banned' is not a
    /// bracket at all; it maps to 0 to flag an illegal list.
    /// </summary>
    private static (int Level, string Label) MapTag(string tag) => tag.ToUpperInvariant() switch
    {
        "B" => (0, "Illegal — contains banned cards"),
        "E" => (1, "Exhibition"),
        "C" => (2, "Core"),
        "O" => (2, "Oddball (between Core and Upgraded)"),
        "P" => (3, "Upgraded"),
        "S" => (4, "Optimized"),
        "R" => (5, "cEDH"),
        _   => (3, $"Unknown tag '{tag}'"),
    };

    private static bool IsBasicLand(string name) => name is
        "Plains" or "Island" or "Swamp" or "Mountain" or "Forest" or "Wastes";
}
