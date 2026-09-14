namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Values are supplied from
/// configuration/user-secrets (never hard-coded): Maxio:ApiKey, Maxio:Subdomain,
/// Maxio:ProductFamilyHandle, Maxio:BaseUrl (optional verbatim override) and the
/// optional Maxio:Environment ("US" or "EU", default US).
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// When set, used verbatim as the API base address instead of deriving one from
    /// the subdomain and environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment, "US" (default) or "EU". Only used when BaseUrl is not set.
    /// </summary>
    public string Environment { get; set; } = "US";
}
