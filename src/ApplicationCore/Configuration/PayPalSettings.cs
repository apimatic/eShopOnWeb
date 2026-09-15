namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed binding of the <c>PayPal:</c> configuration section.
/// Values are supplied via configuration / user-secrets (never hard-coded) and
/// originate from the PAYPAL_* environment variables on the host.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST app client id (PayPal:ClientId / PAYPAL_CLIENT_ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST app secret (PayPal:ClientSecret / PAYPAL_CLIENT_SECRET).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live" (PayPal:Environment / PAYPAL_ENVIRONMENT).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every payment (PayPal:Currency / PAYPAL_CURRENCY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL (PayPal:BaseUrl). When set it is used verbatim
    /// for every PayPal call including the OAuth token request. When empty the base URL
    /// is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// The effective API base address. Honours <see cref="BaseUrl"/> when provided,
    /// otherwise selects the sandbox or live host from <see cref="Environment"/>.
    /// Always ends with a trailing slash so relative request paths resolve correctly.
    /// </summary>
    public string ResolveBaseUrl()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : (IsSandbox ? SandboxBaseUrl : LiveBaseUrl);

        return raw.EndsWith('/') ? raw : raw + "/";
    }

    public bool IsSandbox =>
        string.IsNullOrWhiteSpace(Environment) ||
        Environment.Trim().Equals("sandbox", System.StringComparison.OrdinalIgnoreCase);
}
