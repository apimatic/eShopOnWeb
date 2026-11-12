namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio settings, bound from the <c>Maxio:</c> configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio:ApiKey — the Maxio (Chargify) API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio:Subdomain — the Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Maxio:Environment — "US" or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Maxio:BaseUrl — optional override, used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
