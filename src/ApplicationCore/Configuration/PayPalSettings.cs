namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal settings bound from the "PayPal" configuration section.
/// Values are supplied through configuration (user-secrets / environment) and are never
/// hard-coded so the same build can target a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "production"/"live".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every amount (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL override. When set it is used verbatim for every PayPal
    /// call (including the OAuth token request). When empty, the base URL is derived from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private bool IsProduction =>
        Environment.Trim().ToLowerInvariant() is "production" or "live";

    /// <summary>
    /// Resolves the API base address. Honors <see cref="BaseUrl"/> verbatim when provided,
    /// otherwise derives it from the environment.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return IsProduction
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
