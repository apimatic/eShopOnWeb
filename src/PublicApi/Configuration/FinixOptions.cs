using System;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" => "https://finix.live-payments-api.com",
            _ => "https://finix.sandbox-payments-api.com"
        };
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

        var environment = Environment?.Trim().ToLowerInvariant();
        if (environment is not ("sandbox" or "live"))
        {
            throw new InvalidOperationException("Finix:Environment must be either 'sandbox' or 'live'.");
        }
    }
}
