using System;

namespace Microsoft.eShopWeb.ApplicationCore.Finix;

/// <summary>
/// Bound from the <c>Finix:</c> configuration section. No credential values are
/// hard-coded here; the values come from configuration/user-secrets/environment.
/// </summary>
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

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://finix.live-payments-api.com"
            : "https://finix.sandbox-payments-api.com";
    }

    public string ResolveCurrency()
    {
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("Finix:Currency is not configured.");
        }

        return Currency.Trim().ToUpperInvariant();
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
    }
}
