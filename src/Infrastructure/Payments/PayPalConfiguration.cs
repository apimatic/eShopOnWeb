using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalConfiguration
{
    public static void BindFromEnvironment(IConfiguration configuration)
    {
        CopyIfPresent(configuration, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        CopyIfPresent(configuration, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        CopyIfPresent(configuration, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        CopyIfPresent(configuration, "PAYPAL_CURRENCY", "PayPal:Currency");
        CopyIfPresent(configuration, "PAYPAL_BASE_URL", "PayPal:BaseUrl");
    }

    private static void CopyIfPresent(IConfiguration configuration, string sourceKey, string destinationKey)
    {
        var value = configuration[sourceKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[destinationKey] = value;
        }
    }

    public static string FormatMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static string ToRfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
