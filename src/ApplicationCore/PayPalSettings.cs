using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied through
/// environment variables / user secrets; nothing here is hard-coded in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SECTION_NAME = "PayPal";

    /// <summary>PayPal REST app client id (PayPal:ClientId).</summary>
    public string? ClientId { get; set; }

    /// <summary>PayPal REST app client secret (PayPal:ClientSecret).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default) or "live" (PayPal:Environment).</summary>
    public string? Environment { get; set; }

    /// <summary>Currency used for all payment amounts, e.g. USD (PayPal:Currency).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional override (PayPal:BaseUrl). When set it is used verbatim as the API base
    /// address for every PayPal call - including the token request - instead of the URL
    /// derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolvedBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl.TrimEnd('/');
            }

            var environment = (Environment ?? "sandbox").Trim().ToLowerInvariant();
            return environment switch
            {
                "sandbox" => "https://api-m.sandbox.paypal.com",
                "live" or "production" => "https://api-m.paypal.com",
                _ => throw new InvalidOperationException($"Unknown PayPal environment '{Environment}'. Expected 'sandbox' or 'live', or set PayPal:BaseUrl.")
            };
        }
    }

    public string ResolvedCurrency => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();

    public void ValidateForPayments()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (e.g. via user-secrets from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).");
        }
    }
}
