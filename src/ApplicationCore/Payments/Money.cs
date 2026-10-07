using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Converts between catalog prices (decimal, major units) and the integer minor units payment providers charge,
/// using the ISO 4217 minor-unit exponent of the currency. Conversion is exact or it fails: a total that cannot be
/// expressed to the smallest unit of the currency is never rounded silently.
/// </summary>
public static class Money
{
    // ISO 4217 currencies whose minor unit is not 2 decimals. Everything else listed in Supported has exponent 2.
    private static readonly Dictionary<string, int> NonDefaultExponents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
    };

    private static readonly HashSet<string> TwoDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "AED", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN", "BAM", "BBD", "BDT", "BGN", "BMD", "BND",
        "BOB", "BRL", "BSD", "BWP", "BYN", "BZD", "CAD", "CHF", "CNY", "COP", "CRC", "CUP", "CVE", "CZK", "DKK",
        "DOP", "DZD", "EGP", "ETB", "EUR", "FJD", "GBP", "GEL", "GHS", "GIP", "GMD", "GTQ", "GYD", "HKD", "HNL",
        "HTG", "HUF", "IDR", "ILS", "INR", "JMD", "KES", "KGS", "KHR", "KYD", "KZT", "LAK", "LBP", "LKR", "LRD",
        "LSL", "MAD", "MDL", "MKD", "MMK", "MNT", "MOP", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN", "NAD", "NGN",
        "NIO", "NOK", "NPR", "NZD", "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "QAR", "RON", "RSD", "RUB", "SAR",
        "SBD", "SCR", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "STN", "SZL", "THB", "TJS", "TOP", "TRY", "TTD",
        "TWD", "TZS", "UAH", "USD", "UYU", "UZS", "WST", "XCD", "YER", "ZAR", "ZMW",
    };

    public static bool IsSupportedCurrency(string? currency) =>
        currency is { Length: 3 } && (NonDefaultExponents.ContainsKey(currency) || TwoDecimalCurrencies.Contains(currency));

    public static int MinorUnitExponent(string currency)
    {
        if (!IsSupportedCurrency(currency))
            throw new ArgumentException($"Currency '{currency}' is not supported.", nameof(currency));
        return NonDefaultExponents.TryGetValue(currency, out var exponent) ? exponent : 2;
    }

    public static long ToMinorUnits(decimal amount, string currency)
    {
        var scaled = amount * Pow10(MinorUnitExponent(currency));
        if (scaled != decimal.Truncate(scaled))
            throw new OrderPaymentException(OrderPaymentError.AmountNotRepresentable,
                $"{amount} cannot be expressed in whole minor units of {currency.ToUpperInvariant()}.");
        return decimal.ToInt64(scaled);
    }

    public static decimal FromMinorUnits(long minorUnits, string currency) =>
        minorUnits / Pow10(MinorUnitExponent(currency));

    /// <summary>"40.50 USD" — the amount with exactly the currency's minor-unit digits.</summary>
    public static string Format(long minorUnits, string currency)
    {
        var exponent = MinorUnitExponent(currency);
        return FromMinorUnits(minorUnits, currency).ToString("F" + exponent, System.Globalization.CultureInfo.InvariantCulture)
            + " " + currency.ToUpperInvariant();
    }

    private static decimal Pow10(int exponent) => exponent switch { 0 => 1m, 2 => 100m, 3 => 1000m, _ => throw new ArgumentOutOfRangeException(nameof(exponent)) };
}
