using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Values are supplied from .NET user-secrets / environment variables:
/// Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle and the optional
/// Maxio:BaseUrl override. No values are hard-coded.
/// </summary>
public class MaxioOptions : IValidatableObject
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain. Required unless <see cref="BaseUrl"/> is provided.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plan catalog.
    /// </summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            yield return new ValidationResult(
                "Either Maxio:Subdomain or Maxio:BaseUrl must be configured.",
                new[] { nameof(Subdomain), nameof(BaseUrl) });
        }
    }
}
