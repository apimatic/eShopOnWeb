using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are expected to come
/// from user-secrets (Development) or environment variables — never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing site API key (Basic auth username).
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio Advanced Billing site subdomain (e.g. "cp-exp-2").
    /// </summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base URL override. When set, it is used as-is
    /// instead of deriving the base URL from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}