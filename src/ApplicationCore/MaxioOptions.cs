namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values come from user-secrets or environment variables — never from committed files.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSection = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set, it wins over the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
