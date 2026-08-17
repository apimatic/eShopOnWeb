using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal settings bound from the "PayPal" configuration section. Values are supplied via
/// .NET user-secrets or the PAYPAL_* environment variables — never hard-coded, so the same build can run
/// against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live"/"production". Used to derive the API base URL unless <see cref="BaseUrl"/> is set.</summary>
    public string? Environment { get; set; }

    /// <summary>The transacting currency, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>Optional explicit API base address. When set, it is used verbatim for every PayPal call.</summary>
    public string? BaseUrl { get; set; }

    public string CurrencyCode =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();

    /// <summary>
    /// Resolves the API base address. When <see cref="BaseUrl"/> is set it is used verbatim (including for the
    /// token request); otherwise it is derived from <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = (Environment ?? "sandbox").Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
