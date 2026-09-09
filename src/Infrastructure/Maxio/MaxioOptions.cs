namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section. Secrets are supplied via .NET user-secrets or environment variables; no values
/// are stored in the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (used as the Basic-auth username).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain, e.g. "my-site" for https://my-site.chargify.com.
    /// Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the product family that contains the purchasable subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
