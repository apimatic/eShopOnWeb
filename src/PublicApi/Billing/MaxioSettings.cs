namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings of the Maxio integration, bound from the <c>Maxio:</c> configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override; used verbatim as the API's base address when set.</summary>
    public string? BaseUrl { get; set; }
}
