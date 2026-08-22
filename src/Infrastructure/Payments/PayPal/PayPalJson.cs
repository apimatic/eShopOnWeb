using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

internal static class PayPalMoneyFormat
{
    private static readonly HashSet<string> ZeroDecimal = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    public static string Format(decimal amount, string currency)
    {
        var decimals = ZeroDecimal.Contains(currency) ? 0 : 2;
        return decimal.Round(amount, decimals, System.MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
