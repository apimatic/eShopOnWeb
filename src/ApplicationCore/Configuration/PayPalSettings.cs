namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal settings, bound from the "PayPal" configuration section. Values are supplied via
/// .NET user-secrets / environment and are never hard-coded, so the same build runs against
/// a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live" (or "production"). Ignored when <see cref="BaseUrl"/> is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every amount (from catalog prices).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for
    /// EVERY PayPal call — including the OAuth token request — instead of deriving one from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
