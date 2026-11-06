namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section.
/// Values come from configuration (user-secrets / environment) and are never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
