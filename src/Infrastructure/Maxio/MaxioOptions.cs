namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section. Secret values (ApiKey) and site-specific values are supplied via user-secrets or
/// environment variables and must never be committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing site API key (used as the Basic-auth user name).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Advanced Billing site subdomain, e.g. "acme" for https://acme.chargify.com.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans exposed by this API.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, it is used verbatim as the API base address instead of deriving
    /// one from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional hosting environment: "US" (default) or "EU". Only used when BaseUrl is not set.
    /// </summary>
    public string Environment { get; set; } = "US";
}
