using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class PayPalMoney
{
    public static string ToValue(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
