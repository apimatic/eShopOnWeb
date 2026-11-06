namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the "Maxio:" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio (Chargify) API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>"US" or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
