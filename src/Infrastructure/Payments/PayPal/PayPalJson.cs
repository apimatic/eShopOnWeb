using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string FormatMoney(decimal amount, string currency)
    {
        var rounded = decimal.Round(amount, DecimalPlaces(currency), MidpointRounding.AwayFromZero);
        var places = DecimalPlaces(currency);
        return rounded.ToString(places == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
    }

    public static decimal ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int DecimalPlaces(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "JPY" or "HUF" or "TWD" or "KRW" => 0,
            _ => 2
        };
    }
}
