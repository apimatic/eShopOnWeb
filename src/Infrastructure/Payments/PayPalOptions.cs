using System;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalOptions : IPayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    string IPayPalSettings.Currency => Currency;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var environment = Environment?.Trim();
        if (string.Equals(environment, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }
}
