using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException("Finix:Username is not configured. Set FINIX_USERNAME or Finix:Username.");
        if (string.IsNullOrWhiteSpace(Password))
            throw new InvalidOperationException("Finix:Password is not configured. Set FINIX_PASSWORD or Finix:Password.");
        if (string.IsNullOrWhiteSpace(MerchantId))
            throw new InvalidOperationException("Finix:MerchantId is not configured. Set FINIX_MERCHANT_ID or Finix:MerchantId.");
        if (string.IsNullOrWhiteSpace(Currency))
            throw new InvalidOperationException("Finix:Currency is not configured. Set FINIX_CURRENCY or Finix:Currency.");

        _ = ResolveBaseUrl();
    }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment.ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com",
            "live" => "https://finix.live-payments-api.com",
            _ => throw new InvalidOperationException("Finix:Environment must be 'sandbox' or 'live' (or set Finix:BaseUrl).")
        };
    }
}
