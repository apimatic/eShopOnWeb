using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Presentation helpers for the money and billing-cadence values returned by the billing provider.
/// Amounts travel as integer minor units; the decimal and display forms exist so a client does not have to
/// know how many minor units the currency has.
/// </summary>
internal static class SubscriptionMoney
{
    public static decimal ToDecimal(long amountInCents) => amountInCents / 100m;

    /// <summary>Formats an amount as, for example, <c>299.00 USD</c>.</summary>
    public static string Format(long amountInCents, string? currency)
    {
        var amount = ToDecimal(amountInCents).ToString("0.00", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(currency) ? amount : $"{amount} {currency!.ToUpperInvariant()}";
    }

    /// <summary>Formats a recurring price as, for example, <c>299.00 USD / month</c> or <c>.../ 3 months</c>.</summary>
    public static string FormatRecurring(long amountInCents, string? currency, int interval, string? intervalUnit)
    {
        var price = Format(amountInCents, currency);
        var cadence = FormatInterval(interval, intervalUnit);

        return cadence is null ? price : $"{price} / {cadence}";
    }

    private static string? FormatInterval(int interval, string? intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit) || interval <= 0)
        {
            return null;
        }

        var unit = intervalUnit!.Trim().ToLowerInvariant();
        return interval == 1 ? unit : $"{interval.ToString(CultureInfo.InvariantCulture)} {unit}s";
    }
}
