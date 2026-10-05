using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>ISO-4217 minor units, so an amount is sent to the processor exactly, to the smallest unit.</summary>
public static class CurrencyRules
{
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    public static int MinorUnits(string currency) =>
        ZeroDecimal.Contains(currency) ? 0 : ThreeDecimal.Contains(currency) ? 3 : 2;

    public static bool IsRepresentable(decimal amount, string currency) =>
        decimal.Round(amount, MinorUnits(currency)) == amount;

    public static void EnsureRepresentable(decimal amount, string currency, string what)
    {
        if (!IsRepresentable(amount, currency))
            throw new PaymentValidationException(
                $"{what} {amount.ToString(CultureInfo.InvariantCulture)} cannot be expressed exactly in {currency} ({MinorUnits(currency)} decimal places).");
    }

    /// <summary>Formats an amount with exactly the currency's minor units, invariant culture (e.g. "12.30").</summary>
    public static string Format(decimal amount, string currency)
    {
        var digits = MinorUnits(currency);
        if (decimal.Round(amount, digits) != amount)
            throw new ArgumentException($"Amount {amount} has more precision than {currency} allows.", nameof(amount));
        return amount.ToString("F" + digits, CultureInfo.InvariantCulture);
    }

    public static decimal? Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
