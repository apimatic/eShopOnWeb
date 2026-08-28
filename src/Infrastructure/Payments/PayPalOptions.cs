using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalOptions
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
            return BaseUrl;
        }

        return Environment.ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" => "https://api-m.paypal.com",
            _ => throw new PayPalConfigurationException(
                "PayPal:Environment must be 'sandbox' or 'live', or PayPal:BaseUrl must be set.")
        };
    }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new PayPalConfigurationException(
                "PayPal credentials are missing. Configure PayPal:ClientId and PayPal:ClientSecret.");
        }

        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3)
        {
            throw new PayPalConfigurationException("PayPal:Currency must be a three-letter currency code.");
        }

        _ = ResolveBaseUrl();
    }
}

public sealed class PayPalConfigurationException : Exception
{
    public PayPalConfigurationException(string message) : base(message) { }
}
