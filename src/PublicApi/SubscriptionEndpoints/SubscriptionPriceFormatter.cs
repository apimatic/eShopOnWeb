using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Renders a recurring price the same way everywhere. Currency-code notation is used rather than a
/// symbol because the billing site decides the currency at runtime.
/// </summary>
public static class SubscriptionPriceFormatter
{
    public static string Recurring(long priceInCents, string currency, int interval, string intervalUnit)
    {
        var amount = (priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var money = string.IsNullOrWhiteSpace(currency) ? amount : $"{currency} {amount}";

        if (interval <= 0 || string.IsNullOrWhiteSpace(intervalUnit))
        {
            return money;
        }

        var period = interval == 1 ? intervalUnit : $"{interval} {intervalUnit}s";
        return $"{money} / {period}";
    }
}
