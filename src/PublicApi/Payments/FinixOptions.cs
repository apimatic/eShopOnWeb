using System;
using FinixApiReference.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Bound from the <c>Finix</c> configuration section. The values are supplied by the
/// deployment (environment variables / user-secrets), never hard-coded here.
/// </summary>
public class FinixOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>The Finix currency enum resolved from <see cref="Currency"/> at startup.</summary>
    public Currency FinixCurrency { get; private set; } = FinixApiReference.Models.Enums.Currency.Usd;

    public bool IsSandbox => string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase);
    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fails the host at startup rather than at the first Finix call. Messages name the
    /// missing key only; secret values are never echoed.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException("Finix:Username is not configured. Set FINIX_USERNAME or user-secrets Finix:Username.");
        if (string.IsNullOrWhiteSpace(Password))
            throw new InvalidOperationException("Finix:Password is not configured. Set FINIX_PASSWORD or user-secrets Finix:Password.");
        if (string.IsNullOrWhiteSpace(MerchantId))
            throw new InvalidOperationException("Finix:MerchantId is not configured. Set FINIX_MERCHANT_ID or user-secrets Finix:MerchantId.");

        if (!IsSandbox && !IsLive)
            throw new InvalidOperationException("Finix:Environment must be 'sandbox' or 'live'.");

        var currencyCode = (Currency ?? string.Empty).Trim().ToUpperInvariant();
        if (!FinixApiReference.Models.Enums.Currency.TryGetKnownValue(currencyCode, out var currency))
            throw new InvalidOperationException("Finix:Currency must be a supported ISO 4217 code (USD or CAD).");
        FinixCurrency = currency;

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("Finix:BaseUrl must be an absolute http(s) URL when set.");
            }
        }
        else if (IsLive)
        {
            throw new InvalidOperationException("Finix:BaseUrl is required when Finix:Environment is 'live' because the SDK declares no live host.");
        }
    }
}
