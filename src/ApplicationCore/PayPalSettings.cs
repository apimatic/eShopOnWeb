namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section. None of
/// these values are hard-coded anywhere; the same build runs against any PayPal account by
/// supplying different configuration.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_SECTION = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production". Used to derive the base url when BaseUrl is unset.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO currency code used for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base url override. When set, it is used verbatim for every PayPal call —
    /// including the token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolve the effective API base url (no trailing slash).</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com"
        };
    }
}
