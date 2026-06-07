using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Ingestion.Dto;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace MtgDeckEngine.Ingestion.Mappers;

public static class EdhrecToRdfMapper
{
    /// <summary>
    /// Flatten an EDHREC page into per-category entries keyed by card name.
    /// Resolution from name → oracle id happens later via the cardName index.
    /// </summary>
    public static IEnumerable<EdhrecCardEntry> ToEntries(EdhrecPage page, string commanderSlug)
    {
        var lists = page.Container?.JsonDict?.Cardlists;
        if (lists is null) yield break;
        foreach (var list in lists)
        {
            var categoryLabel = list.Header ?? list.Tag ?? "Unknown";
            var categorySlug = MtgVocab.Slugify(list.Tag ?? list.Header ?? "unknown");
            if (list.Cardviews is null) continue;
            foreach (var c in list.Cardviews)
            {
                // EDHREC's `inclusion` field is the raw deck count, not a
                // percentage. Compute the actual rate from num_decks /
                // potential_decks (in %).
                var pct = c.PotentialDecks > 0
                    ? Math.Round(100m * c.NumDecks / c.PotentialDecks, 2)
                    : 0m;

                yield return new EdhrecCardEntry(
                    Name: c.Name,
                    CommanderSlug: commanderSlug,
                    Category: categorySlug,
                    InclusionPct: pct,
                    SynergyScore: c.Synergy,
                    NumDecks: c.NumDecks,
                    PotentialDecks: c.PotentialDecks)
                {
                    CategoryLabel = categoryLabel
                };
            }
        }
    }

    /// <summary>
    /// Asserts the EDHREC signal triples into a commander-scoped named graph.
    /// Each card is referenced by oracle id — the caller must resolve name → id
    /// via Scryfall first and pass in <paramref name="nameToOracleId"/>.
    /// </summary>
    public static void AssertEntries(
        IGraph g,
        IEnumerable<EdhrecCardEntry> entries,
        IReadOnlyDictionary<string, string> nameToOracleId)
    {
        var rdfType = g.CreateUriNode(new Uri(RdfSpecsHelper.RdfType));
        var rdfsLabel = g.CreateUriNode(new Uri("http://www.w3.org/2000/01/rdf-schema#label"));
        var categoryClass = g.CreateUriNode(new Uri(MtgVocab.Class("Category")));
        var inCategoryProp = g.CreateUriNode(new Uri(MtgVocab.Property("inCategory")));
        var hasInclusionProp = g.CreateUriNode(new Uri(MtgVocab.Property("hasInclusionPct")));
        var hasSynergyProp = g.CreateUriNode(new Uri(MtgVocab.Property("hasSynergyScore")));
        var hasNumDecksProp = g.CreateUriNode(new Uri(MtgVocab.Property("hasNumDecks")));
        var hasPotentialProp = g.CreateUriNode(new Uri(MtgVocab.Property("hasPotentialDecks")));

        foreach (var e in entries)
        {
            if (!nameToOracleId.TryGetValue(e.Name, out var oid)) continue;
            var card = g.CreateUriNode(new Uri(MtgVocab.CardUri(oid)));
            var category = g.CreateUriNode(new Uri(MtgVocab.CategoryUri(e.Category)));
            g.Assert(category, rdfType, categoryClass);
            if (!string.IsNullOrEmpty(e.CategoryLabel))
                g.Assert(category, rdfsLabel, g.CreateLiteralNode(e.CategoryLabel));
            g.Assert(card, inCategoryProp, category);
            g.Assert(card, hasInclusionProp, Decimal(g, e.InclusionPct));
            g.Assert(card, hasSynergyProp, Decimal(g, e.SynergyScore));
            g.Assert(card, hasNumDecksProp, Int(g, e.NumDecks));
            g.Assert(card, hasPotentialProp, Int(g, e.PotentialDecks));
        }
    }

    private static ILiteralNode Decimal(IGraph g, decimal d) =>
        g.CreateLiteralNode(d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode Int(IGraph g, int i) =>
        g.CreateLiteralNode(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeInteger));
}
