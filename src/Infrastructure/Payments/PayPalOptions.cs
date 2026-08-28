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

    public Uri GetBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var overrideUri))
            {
                throw new InvalidOperationException("PayPal:BaseUrl must be an absolute URL.");
            }
            return overrideUri;
        }

        return Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => new Uri("https://api-m.sandbox.paypal.com"),
            "live" => new Uri("https://api-m.paypal.com"),
            _ => throw new InvalidOperationException("PayPal:Environment must be 'sandbox' or 'live'.")
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is required.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is required.");
        if (string.IsNullOrWhiteSpace(Environment))
            throw new InvalidOperationException("PayPal:Environment is required.");
        if (Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-letter currency code.");
        _ = GetBaseUri();
    }
}
