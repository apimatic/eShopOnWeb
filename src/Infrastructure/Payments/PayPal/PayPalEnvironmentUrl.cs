using System;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public static class PayPalEnvironmentUrl
{
    public const string Sandbox = "https://api-m.sandbox.paypal.com";
    public const string Live = "https://api-m.paypal.com";

    public static string Resolve(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.Trim().TrimEnd('/');
        }

        var environment = options.Environment?.Trim() ?? string.Empty;
        if (environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            return Live;
        }

        return Sandbox;
    }
}
