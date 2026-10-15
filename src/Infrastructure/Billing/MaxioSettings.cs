namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Bound from the <c>Maxio:</c> configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic-auth username). Secret: user-secrets / environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the plans on offer.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}
