using System;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// Bound from the "Finix" configuration section. Credential values are never hard-coded;
/// they come from configuration/environment (and are loaded into user-secrets for local runs).
/// </summary>
public class FinixSettings
{
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
                "Finix:Environment must be 'sandbox' or 'live' when Finix:BaseUrl is not set.")
        };
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(MerchantId) ||
            string.IsNullOrWhiteSpace(Environment) ||
            string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException(
                "Finix configuration is incomplete. Configure Finix:Username, Finix:Password, " +
                "Finix:MerchantId, Finix:Environment and Finix:Currency (for example through user-secrets).");
        }

        if (!string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Finix:Environment must be 'sandbox' or 'live'.");
        }
    }
}
