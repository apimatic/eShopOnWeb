namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
