using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return "https://finix.sandbox-payments-api.com";
        }

        if (string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            return "https://finix.live-payments-api.com";
        }

        throw new InvalidOperationException("Finix:Environment must be either 'sandbox' or 'live'.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException("Finix:Username is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException("Finix:Password is not configured.");
        }

        if (string.IsNullOrWhiteSpace(MerchantId))
        {
            throw new InvalidOperationException("Finix:MerchantId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("Finix:Currency is not configured.");
        }

        _ = ResolveBaseUrl();
    }

    public string NormalizedCurrency => Currency.Trim().ToUpperInvariant();
}
