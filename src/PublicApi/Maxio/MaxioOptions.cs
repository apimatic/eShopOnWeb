namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Values are supplied per
/// environment (user secrets in development, environment-derived configuration otherwise);
/// they are never hard-coded in the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key with full access. Bound from <c>Maxio:ApiKey</c>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain. The API base address is derived from it unless
    /// <see cref="BaseUrl"/> is set. Bound from <c>Maxio:Subdomain</c>.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// Bound from <c>Maxio:ProductFamilyHandle</c>.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override. When set it replaces the
    /// subdomain-derived base address. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
