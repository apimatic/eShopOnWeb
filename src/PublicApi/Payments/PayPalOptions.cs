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

    public Uri ResolveBaseUri()
    {
        var value = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.sandbox.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be 'sandbox' or 'live'.");

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("PayPal:BaseUrl must be an absolute URL.");

        return new Uri(uri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is required.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is required.");
        if (Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 code.");
        Currency = Currency.ToUpperInvariant();
        _ = ResolveBaseUri();
    }
}
