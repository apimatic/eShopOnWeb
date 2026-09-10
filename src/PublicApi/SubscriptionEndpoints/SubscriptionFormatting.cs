using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Presentation helpers for rendering subscription prices and billing periods.</summary>
internal static class SubscriptionFormatting
{
    /// <summary>Formats integer cents as a currency amount, e.g. 29900 + "USD" => "$299.00".</summary>
    public static string FormatPrice(long priceInCents, string currency)
    {
        var amount = priceInCents / 100m;
        return currency?.ToUpperInvariant() switch
        {
            "USD" => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture),
            _ => amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + (currency ?? string.Empty).Trim(),
        };
    }

    /// <summary>Short suffix for a billing interval unit, e.g. "month" => "/mo".</summary>
    public static string FormatPeriodSuffix(string? intervalUnit) => intervalUnit?.ToLowerInvariant() switch
    {
        "month" => "/mo",
        "day" => "/day",
        "year" => "/yr",
        "week" => "/wk",
        null or "" => string.Empty,
        _ => "/" + intervalUnit,
    };
}
