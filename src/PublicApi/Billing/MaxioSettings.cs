namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio connection settings, bound from the "Maxio:" configuration section.
/// Values come from configuration only (user-secrets / environment) - never from source.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    /// <summary>"US" or "EU".</summary>
    public string? Environment { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
