using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing API.
/// Values are supplied out of configuration (user-secrets / environment variables) and never hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key (used as the Basic-auth username; the password is the literal "x").
    /// </summary>
    [Required]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain (e.g. "cp-exp-4" for https://cp-exp-4.chargify.com).
    /// </summary>
    [Required]
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans.
    /// </summary>
    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>
    /// Optional verbatim override for the API base address (e.g. https://subdomain.chargify.com).
    /// When set it is used as-is; otherwise the base address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; init; }
}
