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

    public Uri GetBaseUri()
    {
        var value = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.sandbox.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live when PayPal:BaseUrl is not set.");

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("PayPal:BaseUrl must be an absolute URI.");
        }

        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret are required.");
        }

        if (Currency.Length != 3)
        {
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 code.");
        }

        _ = GetBaseUri();
    }
}
