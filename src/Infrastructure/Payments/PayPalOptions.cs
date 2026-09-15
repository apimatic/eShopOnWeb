using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            {
                throw new PaymentValidationException("PayPal:BaseUrl must be an absolute URL.");
            }

            return BaseUrl.TrimEnd('/');
        }

        return Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" or "production" => "https://api-m.paypal.com",
            _ => throw new PaymentValidationException(
                "PayPal:Environment must be Sandbox or Live when PayPal:BaseUrl is not set.")
        };
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new PaymentValidationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
        }

        if (Currency.Length != 3)
        {
            throw new PaymentValidationException("PayPal:Currency must be a three-letter ISO-4217 code.");
        }

        _ = GetBaseUrl();
    }
}
