using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "HUF", "TWD", "CLP", "ISK", "UGX", "VND", "XAF", "XOF", "XPF"
    };

    public static string FormatAmount(decimal amount, string currency)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;
        return System.Math.Round(amount, decimals).ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
