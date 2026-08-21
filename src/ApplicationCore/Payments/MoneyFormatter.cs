using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Formats decimal amounts to the string form PayPal expects (fixed to the currency's minor units) and
/// parses PayPal string amounts back to decimal. Ensures a hold/capture equals the order total to the cent.
/// </summary>
public static class MoneyFormatter
{
    // Currencies with no decimal digits, per PayPal's currency support. Anything else defaults to 2.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "HUF", "JPY", "TWD"
    };

    // Currencies PayPal does not accept decimals for at all are treated as 0-decimal above; everything else is 2.
    public static int DecimalDigits(string currencyCode)
        => ZeroDecimalCurrencies.Contains(currencyCode) ? 0 : 2;

    public static string Format(decimal amount, string currencyCode)
    {
        var digits = DecimalDigits(currencyCode);
        return amount.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static decimal? ParseOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (decimal?)null;
    }
}
