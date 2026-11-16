using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "Finix" configuration section using exactly these keys:
/// Finix:Username, Finix:Password, Finix:MerchantId, Finix:Environment,
/// Finix:Currency, Finix:BaseUrl.
/// No credential values are hard-coded here.
/// </summary>
public class FinixSettings
{
    public const string SectionName = "Finix";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }

    /// <summary>Optional override. When set it is used verbatim for every Finix call.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com",
            "live" => "https://finix.live-payments-api.com",
            _ => throw new InvalidOperationException(
                "Finix:Environment must be 'sandbox' or 'live' when Finix:BaseUrl is not configured.")
        };
    }

    public string ResolveCurrency() =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();

    public void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException(
                "Finix credentials are not configured. Set Finix:Username and Finix:Password (FINIX_USERNAME/FINIX_PASSWORD).");
        }

        if (string.IsNullOrWhiteSpace(MerchantId))
        {
            throw new InvalidOperationException(
                "Finix merchant is not configured. Set Finix:MerchantId (FINIX_MERCHANT_ID).");
        }
    }
}
