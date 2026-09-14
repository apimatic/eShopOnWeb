using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the billing provider's minor-unit amounts and interval pairs into strings a client can show
/// without knowing the provider's conventions.
/// </summary>
internal static class SubscriptionFormatting
{
    /// <summary>Converts a minor-unit amount (cents) to its major unit.</summary>
    public static decimal ToMajorUnits(long minorUnits) => minorUnits / 100m;

    /// <summary>
    /// Formats an amount as e.g. <c>299.00 USD</c>. Deliberately invariant and symbol-free: the site's
    /// currency is not known until runtime, and guessing a symbol for an arbitrary code gets it wrong.
    /// </summary>
    public static string FormatMoney(long minorUnits, string? currency)
    {
        var amount = ToMajorUnits(minorUnits).ToString("0.00", CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(currency) ? amount : $"{amount} {currency}";
    }

    /// <summary>Describes a billing period as e.g. <c>every month</c> or <c>every 3 months</c>.</summary>
    public static string DescribeBillingPeriod(int interval, string? intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit) || interval <= 0)
        {
            return string.Empty;
        }

        return interval == 1
            ? $"every {intervalUnit}"
            : $"every {interval.ToString(CultureInfo.InvariantCulture)} {intervalUnit}s";
    }
}
