using System.Text.RegularExpressions;

namespace MtgDeckEngine.Core.Brackets;

/// <summary>
/// A strategy you can ask a deck to lean into, matched against oracle text.
///
/// This exists because clustering a commander's tournament decks does not
/// produce clean archetypes: for Kefka the within-cluster similarity (0.52–0.59)
/// is no better than the similarity between any two of its decks (0.58). Its
/// decks are a spectrum, not three families. Naming groups from that would be
/// inventing sharpness the data does not have.
///
/// Asking for a theme directly sidesteps the question — you say what you want
/// the deck to do, and cards that do it are favoured.
///
/// Matching is keyword regex over oracle text, so it is approximate: a card can
/// read like a theme without playing like one. It is a preference applied to
/// ranking, not a filter, so a mismatch costs a slot rather than breaking the
/// deck.
/// </summary>
public sealed record DeckTheme(string Key, string Name, string Description, Regex Pattern)
{
    public bool Matches(string? oracleText)
        => !string.IsNullOrEmpty(oracleText) && Pattern.IsMatch(oracleText.ToLowerInvariant());

    private static Regex R(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.Singleline);

    public static readonly DeckTheme Wheel = new(
        "wheel", "Wheels",
        "Effects that refill hands — everyone discards and draws again.",
        R(@"(discards? their hand|shuffles? .*hand .*into .*library).*draws?"
        + @"|each player draws"
        + @"|draws? seven cards"));

    public static readonly DeckTheme LifeDrain = new(
        "lifedrain", "Life drain",
        "Opponents lose life while you gain it, a point at a time.",
        R(@"each opponent loses \d+ life"
        + @"|loses? \d+ life.*you gain"
        + @"|you gain .*life.*loses? \d+ life"
        + @"|drains?"
        + @"|target opponent loses \d+ life"));

    public static readonly DeckTheme Tokens = new(
        "tokens", "Tokens",
        "Creating creature tokens and paying them off.",
        R(@"creates? .*token|create \w+ .*creature token|populate"));

    public static readonly DeckTheme Storm = new(
        "storm", "Storm and copies",
        "Chaining cheap spells, copying them, paying off the count.",
        R(@"\bstorm\b|copy target .*spell|magecraft|whenever you cast .*(instant|sorcery)"));

    public static readonly DeckTheme Stax = new(
        "stax", "Stax and taxes",
        "Making opponents pay more, untap less, and act on your terms.",
        R(@"unless that player pays|spells? cost \{\d+\} more"
        + @"|don'?t untap|doesn'?t untap|can'?t cast|can'?t attack|skip .*step"));

    public static readonly DeckTheme Blink = new(
        "blink", "Blink",
        "Exiling your own permanents and returning them for their triggers.",
        R(@"exile .*(you control|target creature).*return (it|them|that card).*battlefield"
        + @"|flickers?"));

    public static readonly DeckTheme Sacrifice = new(
        "sacrifice", "Sacrifice",
        "Trading permanents for value, and payoffs when things die.",
        R(@"sacrifice an? (creature|artifact|permanent)|whenever .*(dies|is put into a graveyard)"));

    public static readonly DeckTheme Counters = new(
        "counters", "+1/+1 counters",
        "Accumulating counters and multiplying them.",
        R(@"\+1/\+1 counter|proliferate"));

    public static readonly DeckTheme Graveyard = new(
        "graveyard", "Graveyard",
        "Using the graveyard as a resource rather than a bin.",
        R(@"from your graveyard|return .*from .*graveyard.*(battlefield|hand)|flashback|escape|delve"));

    public static readonly IReadOnlyList<DeckTheme> All =
        [Wheel, LifeDrain, Tokens, Storm, Stax, Blink, Sacrifice, Counters, Graveyard];

    public static IReadOnlyList<DeckTheme> Resolve(IEnumerable<string>? keys)
        => keys is null
            ? []
            : All.Where(t => keys.Contains(t.Key, StringComparer.OrdinalIgnoreCase)).ToList();
}
