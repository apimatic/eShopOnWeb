namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration options for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key, used as the basic-auth username (password is fixed to "x" per the OpenAPI spec).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain of the Advanced Billing site (the {site} variable in the spec's server template).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans offered by the shop.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override for the API base address. When set, it is used as-is
    /// instead of deriving the base URL from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}