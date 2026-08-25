namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Bound from the "PayPal" configuration section. Values come from user-secrets/environment
/// variables only -- never hard-coded, so the same build can run against a different PayPal
/// account than the one used in development.
/// </summary>
public class PayPalOptions
{
    public const string ConfigSectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Does not map onto the SDK's own environment enum (which has
    /// only a sandbox member); this selects the default base URL for the sandbox server slot.</summary>
    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>Optional override applied verbatim as the API base address for every PayPal call,
    /// including the OAuth token request. Takes precedence over <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }
}
