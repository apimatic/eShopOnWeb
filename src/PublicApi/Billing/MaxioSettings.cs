namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Bound from the "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment, Maxio:BaseUrl).
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    /// <summary>"US" (default) or "EU".</summary>
    public string? Environment { get; set; }
    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
