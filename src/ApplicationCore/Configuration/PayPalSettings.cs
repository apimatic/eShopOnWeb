namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal:" configuration section. Values are supplied via user-secrets /
/// environment and must never be hard-coded in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live"/"production". Used to derive the API base URL when BaseUrl is unset.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base URL, honoring the BaseUrl override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var env = (Environment ?? "sandbox").Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
