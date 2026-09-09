using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values are supplied via user-secrets /
/// environment variables and are never stored in the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (basic-auth username).
    /// </summary>
    [Required]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain; the API base URL is derived from it unless <see cref="BaseUrl"/> is set.
    /// </summary>
    [Required]
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; init; }
}
