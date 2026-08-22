using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

public static class PayPalMoneyFormat
{
    public static string ToApiValue(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal FromApiValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static long ToCents(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, System.MidpointRounding.AwayFromZero);
}
