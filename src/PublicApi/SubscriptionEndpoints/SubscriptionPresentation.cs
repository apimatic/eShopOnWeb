using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Presentation helpers for turning Maxio's integer-cents / interval data into user-facing strings.
/// </summary>
internal static class SubscriptionPresentation
{
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    public static string FormatPrice(long cents) => (cents / 100m).ToString("C", Usd);

    public static string FormatCadence(int interval, string? intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return string.Empty;
        }

        return interval <= 1 ? intervalUnit : $"{interval} {intervalUnit}s";
    }

    public static string FormatBillingSummary(long cents, int interval, string? intervalUnit)
    {
        var cadence = FormatCadence(interval, intervalUnit);
        var price = FormatPrice(cents);
        return string.IsNullOrEmpty(cadence) ? price : $"{price} / {cadence}";
    }
}
