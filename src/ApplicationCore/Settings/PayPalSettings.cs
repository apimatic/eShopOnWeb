namespace Microsoft.eShopWeb.ApplicationCore.Settings;

/// <summary>
/// Bound from the "PayPal:" configuration section. Values come from configuration /
/// user-secrets / environment variables and are never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SECTION_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (supported) or "live"/"production" (requires BaseUrl override).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 code used for every payment operation, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional verbatim API base-address override applied to every PayPal call,
    /// including the OAuth token request. Empty means: derive from Environment.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
