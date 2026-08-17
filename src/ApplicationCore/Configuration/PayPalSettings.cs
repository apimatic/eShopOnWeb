namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> section. Values are never hard-coded and never
/// committed; they are supplied via .NET user-secrets / environment so the same build runs against any
/// PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Ignored when <see cref="BaseUrl"/> is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency used for every amount (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for <em>every</em> PayPal call
    /// (including the OAuth token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base URL, honouring the <see cref="BaseUrl"/> override.</summary>
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
