using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class MoneyFormatter
{
    public static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, System.MidpointRounding.AwayFromZero);

    public static string ToPayPalValue(decimal amount) =>
        Round(amount).ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
