using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Small helpers for rendering subscription pricing in a friendly way.
/// </summary>
internal static class SubscriptionDisplay
{
    public static string FormatPrice(decimal price, string currency, string interval, int intervalCount)
    {
        var amount = currency == "USD"
            ? "$" + price.ToString("0.00", CultureInfo.InvariantCulture)
            : price.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;

        var period = intervalCount <= 1
            ? interval
            : intervalCount + " " + interval + "s";

        return amount + "/" + period;
    }
}
