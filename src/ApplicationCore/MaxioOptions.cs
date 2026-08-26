namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Values are supplied at runtime via user-secrets or
/// environment variables; none are stored in the repository.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
