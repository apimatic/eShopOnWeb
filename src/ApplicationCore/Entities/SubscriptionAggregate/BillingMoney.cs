using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Formats billing amounts. The demo catalog is priced in US dollars, so amounts are rendered with a fixed
/// culture rather than the server's — otherwise the same price would display as "$299.00" on one machine
/// and with a different currency symbol on another.
/// </summary>
public static class BillingMoney
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Renders an amount as currency, e.g. <c>$299.00</c>.</summary>
    public static string ToDisplay(decimal amount) => amount.ToString("C", DisplayCulture);

    /// <summary>Renders an amount with a leading sign, e.g. <c>+$22.50</c> or <c>-$4.10</c>.</summary>
    public static string ToSignedDisplay(decimal amount) =>
        amount == 0m ? ToDisplay(0m) : (amount > 0m ? "+" : "-") + ToDisplay(System.Math.Abs(amount));
}
