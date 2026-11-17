using System;
using System.Collections.Generic;
using FinixApiReference.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Settings bound from the <c>Finix:</c> configuration section. Every value is supplied by
/// configuration/environment/user-secrets; none is hard-coded here.
/// </summary>
public sealed class FinixSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call budget applied to every Finix request, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    private const string SandboxUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveUrl = "https://finix.live-payments-api.com";

    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates every credential/config value, failing startup rather than the first request.</summary>
    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Username)) missing.Add("Finix:Username");
        if (string.IsNullOrWhiteSpace(Password)) missing.Add("Finix:Password");
        if (string.IsNullOrWhiteSpace(MerchantId)) missing.Add("Finix:MerchantId");
        if (string.IsNullOrWhiteSpace(Environment)) missing.Add("Finix:Environment");
        if (string.IsNullOrWhiteSpace(Currency)) missing.Add("Finix:Currency");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Finix payment configuration is incomplete. Set: {string.Join(", ", missing)}. " +
                "These are bound from the Finix: configuration section (for example via user-secrets or the FINIX_* environment variables).");
        }

        if (!string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase) && !IsLive)
        {
            throw new InvalidOperationException(
                "Finix:Environment must be either 'sandbox' or 'live'.");
        }

        if (!FinixApiReference.Models.Enums.Currency.TryGetKnownValue(Currency, out _))
        {
            throw new InvalidOperationException(
                $"Finix:Currency '{Currency}' is not supported by the Finix SDK (expected USD or CAD).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException(
                "Finix:BaseUrl, when set, must be an absolute http(s) URL.");
        }

        if (TimeoutSeconds <= 0 || TimeoutSeconds > 300)
        {
            throw new InvalidOperationException("Finix:TimeoutSeconds must be between 1 and 300.");
        }
    }

    /// <summary>
    /// The API base address for every Finix call. An explicit <see cref="BaseUrl"/> wins verbatim;
    /// otherwise it is derived from the configured environment.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        return IsLive ? LiveUrl : SandboxUrl;
    }

    public Currency ResolveCurrency()
    {
        if (FinixApiReference.Models.Enums.Currency.TryGetKnownValue(Currency, out var known))
        {
            return known;
        }

        throw new InvalidOperationException(
            $"Finix:Currency '{Currency}' is not supported by the Finix SDK (expected USD or CAD).");
    }
}
