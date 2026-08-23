using System.Globalization;

namespace MtgDeckEngine.Core;

/// <summary>
/// Lexical forms for typed RDF literals.
/// </summary>
public static class RdfLiterals
{
    /// <summary>
    /// Canonical <c>xsd:decimal</c> lexical form: a decimal point with exactly
    /// one digit after it at minimum, and no trailing zeros beyond that.
    ///
    /// This is not cosmetic. Jena accepts a non-canonical literal on INSERT and
    /// hands back the canonical form on SELECT, but <c>DELETE ... WHERE</c>
    /// then fails to match it — the operation reports success and the triple
    /// stays. Anything relying on delete-before-insert (prices, commander
    /// aggregates, deleting a saved deck) silently leaks stale data.
    ///
    /// <c>decimal.ToString()</c> is non-canonical for whole numbers ("60") and
    /// for values carrying trailing scale ("1.50"), both of which C# produces
    /// routinely — 60m from JSON, 1.50m from a price feed.
    /// </summary>
    public static string Decimal(decimal value)
        => value.ToString("0.0###########################", CultureInfo.InvariantCulture);
}
