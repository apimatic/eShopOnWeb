namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section; values come from user-secrets / environment
/// and are never hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (used as the Basic auth username; the password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio Advanced Billing site subdomain.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Maxio environment, "US" (default) or "EU". Determines the default host.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// The handle of the product family that holds the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from the subdomain and environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
