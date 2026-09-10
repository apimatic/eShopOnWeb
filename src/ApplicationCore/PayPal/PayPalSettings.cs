namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// PayPal settings bound from the "PayPal:" configuration section. Values are supplied via
/// user-secrets / environment and are never hard-coded, so the same build can run against a
/// different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Settlement currency, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal
    /// call (including the token request); otherwise the base is derived from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base URL, honouring an explicit override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var env = (Environment ?? string.Empty).Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
