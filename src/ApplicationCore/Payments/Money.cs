using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Helpers for formatting/parsing PayPal money values as culture-invariant strings.</summary>
public static class Money
{
    public static string Format(decimal value, int decimals)
        => value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? 0m
            : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
