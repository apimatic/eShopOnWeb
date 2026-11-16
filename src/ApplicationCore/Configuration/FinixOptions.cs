using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

public class FinixOptions
{
    public const string SectionName = "Finix";

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

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com",
            "live" => "https://finix.live-payments-api.com",
            _ => throw new InvalidOperationException(
                "Finix is not configured. Set Finix:Environment to 'sandbox' or 'live', or set Finix:BaseUrl.")
        };
    }

    public string ResolveCurrency()
    {
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("Finix:Currency is not configured.");
        }

        return Currency.Trim().ToUpperInvariant();
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException("Finix API credentials are not configured.");
        }

        if (string.IsNullOrWhiteSpace(MerchantId))
        {
            throw new InvalidOperationException("Finix:MerchantId is not configured.");
        }

        _ = ResolveBaseUrl();
        _ = ResolveCurrency();
    }
}
