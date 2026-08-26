namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Values are supplied via user-secrets or environment
/// variables; none are committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string ConfigName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
