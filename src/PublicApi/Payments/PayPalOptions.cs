using System;
using System.Text.RegularExpressions;

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
        Validate();

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("PayPal:BaseUrl must be an absolute URL.");
            }

            return BaseUrl;
        }

        return Environment.ToUpperInvariant() switch
        {
            "SANDBOX" => "https://api-m.sandbox.paypal.com",
            "LIVE" or "PRODUCTION" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException("PayPal:Environment must be Sandbox, Live, or Production.")
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("PayPal:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientSecret is required.");
        }

        if (!Regex.IsMatch(Currency ?? string.Empty, "^[A-Za-z]{3}$"))
        {
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 currency code.");
        }
    }
}
