namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings for the PayPal integration, bound from the <c>PayPal:</c> configuration section.
/// Values are supplied by configuration (user-secrets / environment) and are never hard-coded,
/// so the same build runs against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_SECTION = "PayPal";

    /// <summary>REST API client id (from <c>PayPal:ClientId</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>REST API client secret (from <c>PayPal:ClientSecret</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment (from <c>PayPal:Environment</c>): <c>sandbox</c> or <c>production</c>/<c>live</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Settlement currency (from <c>PayPal:Currency</c>), e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional base-URL override (from <c>PayPal:BaseUrl</c>). When set, it is used verbatim as the
    /// API base address for every PayPal call, including the credential/token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The currency to charge in, defaulting to USD when unset.</summary>
    public string CurrencyCode => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.Trim().ToUpperInvariant();

    /// <summary>True when the configured environment names production/live.</summary>
    public bool IsProduction =>
        string.Equals(Environment?.Trim(), "production", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment?.Trim(), "live", System.StringComparison.OrdinalIgnoreCase);
}
