using MtgDeckEngine.Core;
using MtgDeckEngine.Ingestion.Dto;
using MtgDeckEngine.Ingestion.Mappers;
using VDS.RDF;
using Xunit;
using RdfGraph = VDS.RDF.Graph;

namespace MtgDeckEngine.UnitTests;

public class ScryfallMapperTests
{
    [Fact]
    public void Maps_card_to_dto_and_triples()
    {
        var card = new ScryfallCard
        {
            Name = "Cultivate",
            OracleId = "cultivate-oracle",
            Colors = new() { "G" },
            ColorIdentity = new() { "G" },
            TypeLine = "Sorcery",
            OracleText = "Search your library...",
            Prices = new ScryfallPrices { Eur = "0.30", Usd = "0.25" },
            Legalities = new() { ["commander"] = "legal" }
        };
        var dto = ScryfallToRdfMapper.ToDto(card);
        Assert.Equal("cultivate-oracle", dto.OracleId);
        Assert.Equal(0.30m, dto.PriceEur);
        Assert.True(dto.CommanderLegal);

        var g = new RdfGraph();
        ScryfallToRdfMapper.AssertCard(g, dto);

        var subj = g.CreateUriNode(new Uri(MtgVocab.CardUri("cultivate-oracle")));
        var nameProp = g.CreateUriNode(new Uri(MtgVocab.Property("hasName")));
        var nameTriples = g.GetTriplesWithSubjectPredicate(subj, nameProp);
        Assert.Single(nameTriples);
    }
}
