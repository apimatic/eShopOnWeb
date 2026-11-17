using System;
using System.Collections.Generic;
using FinixApiReference.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Bound from the <c>Finix</c> configuration section. Values are supplied by the deployment
/// (environment variables / user-secrets); none is hard-coded here.
/// </summary>
public sealed class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);

    public string NormalizedCurrency => (Currency ?? string.Empty).Trim().ToUpperInvariant();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Username))
            errors.Add("Finix:Username is not configured.");
        if (string.IsNullOrWhiteSpace(Password))
            errors.Add("Finix:Password is not configured.");
        if (string.IsNullOrWhiteSpace(MerchantId))
            errors.Add("Finix:MerchantId is not configured.");

        if (!string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Finix:Environment must be 'sandbox' or 'live'.");
        }

        if (string.IsNullOrWhiteSpace(Currency))
            errors.Add("Finix:Currency is not configured.");
        else if (!FinixApiReference.Models.Enums.Currency.TryGetKnownValue(NormalizedCurrency, out _))
            errors.Add($"Finix:Currency '{NormalizedCurrency}' is not a currency this build supports (USD or CAD).");

        return errors;
    }

    public Currency ResolveCurrency()
    {
        if (!FinixApiReference.Models.Enums.Currency.TryGetKnownValue(NormalizedCurrency, out var currency))
            throw new InvalidOperationException(
                $"Finix:Currency '{NormalizedCurrency}' is not a currency this build supports (USD or CAD).");
        return currency;
    }
}
