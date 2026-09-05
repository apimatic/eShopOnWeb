namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Settings for connecting to Maxio Advanced Billing. Bound from the "Maxio" configuration
/// section; values must come from configuration/user-secrets, never hard-coded, so the same
/// build can target a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Per-site API key, used as the Basic Auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, e.g. "cp-exp-4".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that holds the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving a base address from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio data-center environment ("US" or "EU"). Defaults to "US".</summary>
    public string Environment { get; set; } = "US";
}
