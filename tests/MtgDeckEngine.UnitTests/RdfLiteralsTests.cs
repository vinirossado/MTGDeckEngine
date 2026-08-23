using MtgDeckEngine.Core;
using Xunit;

namespace MtgDeckEngine.UnitTests;

public class RdfLiteralsTests
{
    [Theory]
    // Whole numbers: decimal.ToString() gives "60", which Jena stores but then
    // cannot match in a DELETE. Needs the point and a digit.
    [InlineData(60, "60.0")]
    [InlineData(0, "0.0")]
    [InlineData(-3, "-3.0")]
    // Trailing scale from a price feed: 1.50m must not stay "1.50".
    [InlineData(1.50, "1.5")]
    [InlineData(2.000, "2.0")]
    // Already canonical: unchanged.
    [InlineData(0.45, "0.45")]
    [InlineData(418.81, "418.81")]
    [InlineData(0.000123, "0.000123")]
    public void Produces_canonical_xsd_decimal(decimal input, string expected)
        => Assert.Equal(expected, RdfLiterals.Decimal(input));

    [Fact]
    public void Always_has_a_decimal_point_with_at_least_one_digit()
    {
        foreach (var v in new decimal[] { 0m, 1m, -1m, 100m, 0.5m, 1234567.89m })
        {
            var s = RdfLiterals.Decimal(v);
            var dot = s.IndexOf('.');
            Assert.True(dot > 0, $"{v} -> '{s}' has no decimal point");
            Assert.True(s.Length - dot - 1 >= 1, $"{v} -> '{s}' has no fractional digit");
        }
    }

    [Fact]
    public void Never_emits_a_trailing_zero_beyond_the_first_fractional_digit()
    {
        foreach (var v in new decimal[] { 1.50m, 2.000m, 0.10m, 3.1400m })
        {
            var s = RdfLiterals.Decimal(v);
            Assert.False(s.Length - s.IndexOf('.') > 2 && s.EndsWith('0'),
                $"{v} -> '{s}' keeps a trailing zero");
        }
    }

    [Fact]
    public void Round_trips_back_to_the_same_value()
    {
        foreach (var v in new decimal[] { 0m, 60m, 1.50m, 0.45m, -3m, 418.81m })
            Assert.Equal(v, decimal.Parse(RdfLiterals.Decimal(v),
                System.Globalization.CultureInfo.InvariantCulture));
    }
}
