using MtgDeckEngine.Core;
using MtgDeckEngine.Core.Models;
using MtgDeckEngine.Ingestion.Dto;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace MtgDeckEngine.Ingestion.Mappers;

public static class ScryfallToRdfMapper
{
    public static CardDto ToDto(ScryfallCard c)
    {
        decimal? priceEur = decimal.TryParse(c.Prices?.Eur, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : null;
        decimal? priceUsd = decimal.TryParse(c.Prices?.Usd, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var u) ? u : null;
        var legal = c.Legalities is not null
            && c.Legalities.TryGetValue("commander", out var v)
            && string.Equals(v, "legal", StringComparison.OrdinalIgnoreCase);
        return new CardDto(
            OracleId: c.OracleId ?? Guid.Empty.ToString(),
            Name: c.Name,
            Colors: c.Colors ?? new(),
            ColorIdentity: c.ColorIdentity ?? new(),
            TypeLine: c.TypeLine ?? "",
            OracleText: c.OracleText,
            PriceEur: priceEur,
            PriceUsd: priceUsd,
            CommanderLegal: legal);
    }

    public static void AssertCard(IGraph g, CardDto card)
    {
        var cardNode = g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId)));
        var rdfType = g.CreateUriNode(new Uri(RdfSpecsHelper.RdfType));

        g.Assert(cardNode, rdfType, g.CreateUriNode(new Uri(MtgVocab.Class("Card"))));

        if (string.Equals(card.TypeLine, "", StringComparison.Ordinal) == false
            && card.TypeLine.Contains("Legendary", StringComparison.Ordinal)
            && card.TypeLine.Contains("Creature", StringComparison.Ordinal)
            && card.CommanderLegal)
        {
            g.Assert(cardNode, rdfType, g.CreateUriNode(new Uri(MtgVocab.Class("Commander"))));
        }

        Assert(g, cardNode, "hasOracleId", g.CreateLiteralNode(card.OracleId));
        Assert(g, cardNode, "hasName",     g.CreateLiteralNode(card.Name));
        if (!string.IsNullOrEmpty(card.TypeLine))
            Assert(g, cardNode, "hasTypeLine", g.CreateLiteralNode(card.TypeLine));
        if (!string.IsNullOrEmpty(card.OracleText))
            Assert(g, cardNode, "hasOracleText", g.CreateLiteralNode(card.OracleText));
        if (card.PriceEur is decimal eur)
            Assert(g, cardNode, "hasPriceEur", Decimal(g, eur));
        if (card.PriceUsd is decimal usd)
            Assert(g, cardNode, "hasPriceUsd", Decimal(g, usd));
        Assert(g, cardNode, "isCommanderLegal", Bool(g, card.CommanderLegal));

        foreach (var c in card.Colors)
            Assert(g, cardNode, "hasColor", g.CreateUriNode(new Uri(MtgVocab.ColorUri(c))));
        foreach (var c in card.ColorIdentity)
            Assert(g, cardNode, "hasColorIdentity", g.CreateUriNode(new Uri(MtgVocab.ColorUri(c))));
    }

    private static void Assert(IGraph g, INode subject, string localProp, INode obj)
        => g.Assert(subject, g.CreateUriNode(new Uri(MtgVocab.Property(localProp))), obj);

    private static ILiteralNode Decimal(IGraph g, decimal d) =>
        g.CreateLiteralNode(d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode Bool(IGraph g, bool b) =>
        g.CreateLiteralNode(b ? "true" : "false", new Uri(XmlSpecsHelper.XmlSchemaDataTypeBoolean));
}
