using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Bound from the "PayPal" configuration section. All values are supplied by configuration
/// (user-secrets / environment variables in this repo) - nothing here is hard-coded so the same
/// build can run against a different PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string ConfigSectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Expected values: "sandbox" or "live". Ignored when <see cref="BaseUrl"/> is set.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency code used for every amount sent to PayPal (e.g. "USD").</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional override. When set, used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request, instead of deriving one from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" => "https://api-m.paypal.com",
            "production" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException(
                $"PayPal:Environment is '{Environment}' but must be 'sandbox' or 'live' (or set PayPal:BaseUrl to override)."),
        };
    }
}
