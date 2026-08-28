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
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return BaseUrl;
        return Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" or "production" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException("PayPal:Environment must be 'sandbox', 'live', or 'production', unless PayPal:BaseUrl is set.")
        };
    }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ClientId)) throw new InvalidOperationException("PayPal:ClientId is required.");
        if (string.IsNullOrWhiteSpace(ClientSecret)) throw new InvalidOperationException("PayPal:ClientSecret is required.");
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO currency code.");
        _ = ResolveBaseUrl();
    }
}
