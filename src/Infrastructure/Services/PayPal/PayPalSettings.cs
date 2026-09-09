namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> configuration section. Values are supplied via
/// user-secrets / environment and are never hard-coded, so the same build can run against a different
/// PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST app client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST app client secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary><c>sandbox</c> or <c>live</c> (from <c>PAYPAL_ENVIRONMENT</c>).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>The currency all amounts are charged in (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim for every PayPal call (including
    /// the token request); otherwise the base URL is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API base address to use, honoring <see cref="BaseUrl"/> when provided.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
