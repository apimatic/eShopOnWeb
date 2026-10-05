using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Converts between decimal amounts and the string amounts processors exchange.</summary>
public static class MoneyFormat
{
    // ISO-4217 currencies without minor units that a merchant is plausibly configured for.
    private static readonly string[] ZeroDecimalCurrencies = { "JPY", "KRW", "VND", "CLP", "ISK", "PYG", "UGX", "XAF", "XOF" };

    public static int DecimalPlaces(string currency) =>
        Array.IndexOf(ZeroDecimalCurrencies, currency.ToUpperInvariant()) >= 0 ? 0 : 2;

    /// <summary>True when <paramref name="amount"/> is representable exactly in <paramref name="currency"/>.</summary>
    public static bool IsExact(decimal amount, string currency) =>
        decimal.Round(amount, DecimalPlaces(currency)) == amount;

    public static string Format(decimal amount, string currency)
    {
        var places = DecimalPlaces(currency);
        if (!IsExact(amount, currency))
            throw new ArgumentException($"{amount} has more precision than {currency} allows.", nameof(amount));
        return amount.ToString(places == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
    }

    public static decimal? Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
