namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Bound from the "Maxio:" configuration section (ApiKey, Subdomain, Environment, BaseUrl).
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
