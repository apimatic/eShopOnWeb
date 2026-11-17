using System;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

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
            return BaseUrl.TrimEnd('/') + "/";
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com/",
            "live" => "https://finix.live-payments-api.com/",
            _ => throw new InvalidOperationException(
                $"Finix:Environment must be 'sandbox' or 'live' (or set Finix:BaseUrl). Current value: '{Environment}'.")
        };
    }

    public string ResolveCurrency()
    {
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException(
                "Finix:Currency is required (for example USD or CAD). Set it from FINIX_CURRENCY.");
        }

        return Currency.Trim().ToUpperInvariant();
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(MerchantId))
        {
            throw new InvalidOperationException(
                "Finix configuration is incomplete. Set Finix:Username, Finix:Password and Finix:MerchantId " +
                "(for example from FINIX_USERNAME, FINIX_PASSWORD and FINIX_MERCHANT_ID).");
        }

        _ = ResolveBaseUrl();
        _ = ResolveCurrency();
    }
}
