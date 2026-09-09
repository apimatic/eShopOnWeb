namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// PayPal configuration, bound from the "PayPal:" configuration section. None of these values
/// are hard-coded anywhere: ClientId/ClientSecret/Environment/Currency come from environment-fed
/// user-secrets, and BaseUrl is an optional override.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Used to derive the API base URL when BaseUrl is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO currency code orders are priced in (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for EVERY PayPal call
    /// (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsLive => string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The effective API base URL (no trailing slash): explicit override, else derived.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return IsLive
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
