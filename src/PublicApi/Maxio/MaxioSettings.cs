namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Secrets come from user-secrets or the
/// environment (Maxio__ApiKey), never from a checked-in file.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}
