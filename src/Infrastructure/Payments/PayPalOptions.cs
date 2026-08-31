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

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configured) || configured.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("PayPal:BaseUrl must be an absolute HTTPS URL.");
            }

            return BaseUrl.TrimEnd('/');
        }

        return Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal credentials are not configured.");
        }

        if (Currency.Length != 3)
        {
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 code.");
        }

        _ = ResolveBaseUrl();
    }
}
