using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return BaseUrl;

        return Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live when PayPal:BaseUrl is not set.");
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 currency code.");
        _ = GetApiBaseUrl();
    }
}
