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

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            var environment = Environment?.Trim().ToLowerInvariant();
            if (environment != "sandbox" && environment != "live")
            {
                throw new InvalidOperationException("Finix:Environment must be 'sandbox' or 'live' when Finix:BaseUrl is not set.");
            }
        }
    }
}
