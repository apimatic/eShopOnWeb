using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixOptions
{
    public const string SectionName = "Finix";
    public const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    public const string LiveBaseUrl = "https://finix.live-payments-api.com";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
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
    }
}
