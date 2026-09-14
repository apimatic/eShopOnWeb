namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the "Maxio" configuration section. Values are sourced from user-secrets/environment
/// in every environment - never hard-code a real API key, subdomain, or product family here.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Advanced Billing API key, used as the Basic-Auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, e.g. "your-company".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of the
    /// default "https://{Subdomain}.chargify.com" derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
