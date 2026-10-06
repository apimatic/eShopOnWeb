namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio (Advanced Billing) integration, bound from the "Maxio"
/// configuration section. Values are supplied via user-secrets / environment and are
/// never hard-coded in the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-4".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead
    /// of deriving "https://{subdomain}.chargify.com" from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
