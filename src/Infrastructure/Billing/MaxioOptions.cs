namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Bound from the "Maxio" configuration section (user-secrets / environment; never committed).</summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional. "US" (default) or "EU".</summary>
    public string? Environment { get; set; }
}
