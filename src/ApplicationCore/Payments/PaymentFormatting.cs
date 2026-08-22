using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PaymentFormatting
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "HUF", "TWD"
    };

    public static string FormatAmount(decimal amount, string currency)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;
        return amount.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static string ToPayPalCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return "c" + hex[..21];
    }
}
