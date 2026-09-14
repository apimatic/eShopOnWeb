using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Renders a billing interval as the phrase a shopper expects to read next to a price.
/// </summary>
public static class BillingPeriodText
{
    public static string Describe(int interval, string? intervalUnit)
    {
        if (interval <= 0 || string.IsNullOrWhiteSpace(intervalUnit))
        {
            return string.Empty;
        }

        var unit = intervalUnit.Trim().ToLowerInvariant();

        return interval == 1
            ? $"every {unit}"
            : $"every {interval.ToString(CultureInfo.InvariantCulture)} {unit}s";
    }
}
