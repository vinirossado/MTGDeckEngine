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
            CommanderLegal: legal,
            ImageUrl: c.ImageUris?.Normal ?? c.ImageUris?.Large ?? c.ImageUris?.Small,
            IsGameChanger: c.GameChanger);
    }

    /// <summary>
    /// Assert the <c>mtg:commander/{slug}</c> node: typed, named, and linked to
    /// the card it comes from.
    ///
    /// This is a different subject from the card. <see cref="AssertCard"/> types
    /// <c>mtg:card/{oracleId}</c> as a Commander when it is a legal one, but
    /// every commander-facing query keys on the slug node — so without this a
    /// commander is invisible to the commander list no matter how much of its
    /// data was ingested.
    ///
    /// <paramref name="slug"/> is passed in rather than derived from the name
    /// because the caller's slug is authoritative: EDHREC's page slug is what
    /// the commander-scoped context graph is named after, and re-deriving it
    /// risks the two drifting apart.
    /// </summary>
    public static void AssertCommanderNode(IGraph g, string slug, CardDto card)
    {
        var commander = g.CreateUriNode(new Uri(MtgVocab.CommanderUri(slug)));
        g.Assert(commander,
            g.CreateUriNode(new Uri(RdfSpecsHelper.RdfType)),
            g.CreateUriNode(new Uri(MtgVocab.Class("Commander"))));
        Assert(g, commander, "hasName", g.CreateLiteralNode(card.Name));
        if (!string.IsNullOrEmpty(card.OracleId))
        {
            Assert(g, commander, "hasOracleId", g.CreateLiteralNode(card.OracleId));
            Assert(g, commander, "isCardOf",
                g.CreateUriNode(new Uri(MtgVocab.CardUri(card.OracleId))));
        }
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
        if (card.IsGameChanger)
            Assert(g, cardNode, "isGameChanger", Bool(g, true));
        if (!string.IsNullOrWhiteSpace(card.ImageUrl))
            Assert(g, cardNode, "hasImageUrl",
                g.CreateLiteralNode(card.ImageUrl!, new Uri(XmlSpecsHelper.XmlSchemaDataTypeAnyUri)));
        Assert(g, cardNode, "isCommanderLegal", Bool(g, card.CommanderLegal));

        foreach (var c in card.Colors)
            Assert(g, cardNode, "hasColor", g.CreateUriNode(new Uri(MtgVocab.ColorUri(c))));
        foreach (var c in card.ColorIdentity)
            Assert(g, cardNode, "hasColorIdentity", g.CreateUriNode(new Uri(MtgVocab.ColorUri(c))));
    }

    private static void Assert(IGraph g, INode subject, string localProp, INode obj)
        => g.Assert(subject, g.CreateUriNode(new Uri(MtgVocab.Property(localProp))), obj);

    private static ILiteralNode Decimal(IGraph g, decimal d) =>
        g.CreateLiteralNode(MtgDeckEngine.Core.RdfLiterals.Decimal(d),
            new Uri(XmlSpecsHelper.XmlSchemaDataTypeDecimal));

    private static ILiteralNode Bool(IGraph g, bool b) =>
        g.CreateLiteralNode(b ? "true" : "false", new Uri(XmlSpecsHelper.XmlSchemaDataTypeBoolean));
}
