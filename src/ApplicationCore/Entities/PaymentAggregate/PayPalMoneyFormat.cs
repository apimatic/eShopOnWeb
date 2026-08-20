using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PayPalMoneyFormat
{
    public static string ToValue(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
