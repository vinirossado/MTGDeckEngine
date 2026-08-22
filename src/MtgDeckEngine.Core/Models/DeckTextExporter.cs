using System.Globalization;
using System.Text;

namespace MtgDeckEngine.Core.Models;

/// <summary>
/// Renders a decklist in the plain "N Card Name" format that Moxfield,
/// Archidekt, MTGGoldfish and MTGO's deck editor all import.
///
/// The commander goes in a trailing block separated by a blank line — that is
/// the convention those importers use to tell the command zone apart from the
/// 99, and without it the commander is silently imported as a maindeck card.
/// </summary>
public static class DeckTextExporter
{
    public static string ToText(
        IEnumerable<CardRecommendation> cards,
        string? commanderName = null)
        => ToText(cards.Select(c => c.Name), commanderName);

    public static string ToText(
        IEnumerable<string> cardNames,
        string? commanderName = null)
    {
        // Collapse duplicates into counts. Basic lands are the main case — a
        // manabase is 30-odd copies of the same four names.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in cardNames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();
            counts[name] = counts.GetValueOrDefault(name) + 1;
            display.TryAdd(name, name);
        }

        var sb = new StringBuilder();
        foreach (var name in counts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(counts[name].ToString(CultureInfo.InvariantCulture))
              .Append(' ')
              .Append(display[name])
              .Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(commanderName))
            sb.Append('\n').Append("1 ").Append(commanderName.Trim()).Append('\n');

        return sb.ToString();
    }
}
