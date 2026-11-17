using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

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

        if (string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            return "https://finix.live-payments-api.com";
        }

        if (string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return "https://finix.sandbox-payments-api.com";
        }

        throw new PaymentOperationException(500,
            "Finix:Environment must be 'sandbox' or 'live' (or set Finix:BaseUrl explicitly).");
    }

    public string NormalizedCurrency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Currency))
            {
                throw new PaymentOperationException(500,
                    "Finix:Currency is not configured. Set Finix:Currency (from FINIX_CURRENCY).");
            }

            return Currency.Trim().ToUpperInvariant();
        }
    }
}

