namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio settings, bound from the "Maxio:" configuration section
/// (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment, Maxio:BaseUrl).
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>"US" or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}
