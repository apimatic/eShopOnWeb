using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" or "production" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException(
                "PayPal:Environment must be 'sandbox' or 'live', unless PayPal:BaseUrl is supplied.")
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret are required.");
        }
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Trim().Length != 3)
        {
            throw new InvalidOperationException("PayPal:Currency must be a three-letter currency code.");
        }
        _ = new Uri(GetBaseUrl(), UriKind.Absolute);
    }
}
