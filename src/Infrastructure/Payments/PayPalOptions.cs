using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new PaymentException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).",
                500);
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new PaymentException(
                "PayPal currency is not configured. Set PayPal:Currency (from PAYPAL_CURRENCY).",
                500);
        }
    }
}
