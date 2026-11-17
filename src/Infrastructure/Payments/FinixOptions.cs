using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class FinixOptions
{
    public const string SectionName = "Finix";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com",
            "live" => "https://finix.live-payments-api.com",
            _ => throw new InvalidOperationException(
                "Finix:Environment must be either 'sandbox' or 'live' when Finix:BaseUrl is not configured.")
        };
    }

    public void EnsureConfigured()
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

        var environment = (Environment ?? string.Empty).Trim().ToLowerInvariant();
        if (environment is not ("sandbox" or "live"))
        {
            throw new InvalidOperationException("Finix:Environment must be either 'sandbox' or 'live'.");
        }

        _ = ResolveBaseUrl();
    }
}
