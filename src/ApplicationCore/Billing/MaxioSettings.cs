namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Bound from the "Maxio" configuration section. Secrets (ApiKey) come from user-secrets / environment only.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>"US" (default) or "EU".</summary>
    public string? Environment { get; set; }
}
