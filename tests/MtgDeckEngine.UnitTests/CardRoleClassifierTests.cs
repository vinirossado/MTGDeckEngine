using MtgDeckEngine.Core.Brackets;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class CardRoleClassifierTests
{
    [Theory]
    // Rocks and dorks — real oracle text.
    [InlineData("Artifact", "{T}: Add {C}{C}.", CardRole.Ramp)]                        // Sol Ring
    [InlineData("Artifact", "{T}: Add one mana of any color.", CardRole.Ramp)]         // Arcane Signet
    [InlineData("Creature — Bird", "{T}: Add one mana of any color.", CardRole.Ramp)]  // Birds of Paradise
    // Land fetch that reaches the battlefield is ramp; a tutor to hand is not.
    [InlineData("Sorcery",
        "Search your library for a basic land card, put it onto the battlefield tapped, then shuffle.",
        CardRole.Ramp)]                                                                 // Rampant Growth
    [InlineData("Sorcery",
        "Search your library for a land card, reveal it, put it into your hand, then shuffle.",
        CardRole.Other)]
    [InlineData("Enchantment", "You may play an additional land on each of your turns.", CardRole.Ramp)]

    // Removal, including counterspells and sweepers.
    [InlineData("Instant", "Destroy target creature. It can't be regenerated.", CardRole.Removal)]
    [InlineData("Sorcery", "Destroy all creatures.", CardRole.Removal)]                 // Wrath of God
    [InlineData("Instant", "Counter target spell.", CardRole.Removal)]
    [InlineData("Instant", "Exile target creature.", CardRole.Removal)]
    [InlineData("Instant", "Beast Within deals no damage. Destroy target permanent.", CardRole.Removal)]

    // Draw.
    [InlineData("Sorcery", "Draw three cards.", CardRole.Draw)]
    [InlineData("Enchantment",
        "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.",
        CardRole.Draw)]                                                                 // Rhystic Study

    // Type line wins for lands, whatever the text says.
    [InlineData("Land", "{T}: Add {U}.", CardRole.Land)]
    [InlineData("Basic Land — Island", "({T}: Add {U}.)", CardRole.Land)]
    [InlineData("Land — Forest Island", "{T}: Add {G} or {U}.", CardRole.Land)]

    // Plain creatures and everything else.
    [InlineData("Creature — Elf Warrior", "Trample.", CardRole.Creature)]
    [InlineData("Enchantment", "Creatures you control get +1/+1.", CardRole.Other)]
    public void Classifies_by_type_line_and_oracle_text(string type, string text, CardRole expected)
        => Assert.Equal(expected, CardRoleClassifier.Classify(type, text));

    [Theory]
    // Wheels are the engine of whole archetypes — Xyris is built on them — and
    // their wording never says "draw a card". Calling them Other misdescribed
    // the deck they define.
    [InlineData("Sorcery", "Each player discards their hand, then draws seven cards.")]        // Wheel of Fortune
    [InlineData("Sorcery",
        "Each player discards their hand, then draws cards equal to the greatest number of cards a player discarded this way.")]  // Windfall
    [InlineData("Sorcery",
        "Each player shuffles their hand and graveyard into their library, then draws seven cards.")]  // Echo of Eons
    [InlineData("Sorcery",
        "Each player shuffles the cards from their hand into their library, then draws that many cards.")]  // Winds of Change
    public void Wheels_count_as_draw(string type, string text)
        => Assert.Equal(CardRole.Draw, CardRoleClassifier.Classify(type, text));

    [Theory]
    // Costs other than tapping still make it ramp.
    [InlineData("Artifact", "Sacrifice a creature: Add one mana of any color.", CardRole.Ramp)]  // Phyrexian Altar
    // Bounce is removal however the target is worded.
    [InlineData("Instant",
        "Return target nonland permanent you don't control to its owner's hand.",
        CardRole.Removal)]                                                                       // Cyclonic Rift
    [InlineData("Artifact",
        "Equipped creature gets +1/-1.\nWhenever equipped creature dies, draw two cards.",
        CardRole.Draw)]                                                                          // Skullclamp
    public void Handles_wordings_the_substring_matcher_missed(string type, string text, CardRole expected)
        => Assert.Equal(expected, CardRoleClassifier.Classify(type, text));

    [Fact]
    public void A_mana_dork_counts_as_ramp_not_as_a_creature()
    {
        // The deck slot it fills is the ramp slot. Counting it as a creature
        // makes every ramp-heavy deck look creature-heavy.
        Assert.Equal(CardRole.Ramp,
            CardRoleClassifier.Classify("Creature — Elf Druid", "{T}: Add {G}."));
    }

    [Fact]
    public void A_removal_creature_counts_as_removal()
    {
        Assert.Equal(CardRole.Removal,
            CardRoleClassifier.Classify(
                "Creature — Human Wizard",
                "When this creature enters, destroy target artifact."));
    }

    [Fact]
    public void Missing_oracle_text_falls_back_to_the_type_line()
    {
        // ~9% of cards have no oracle text in the graph. They must still land
        // somewhere sensible rather than throwing.
        Assert.Equal(CardRole.Creature, CardRoleClassifier.Classify("Creature — Spirit", null));
        Assert.Equal(CardRole.Land, CardRoleClassifier.Classify("Land", null));
        Assert.Equal(CardRole.Other, CardRoleClassifier.Classify("Artifact", null));
        Assert.Equal(CardRole.Other, CardRoleClassifier.Classify(null, null));
    }
}
