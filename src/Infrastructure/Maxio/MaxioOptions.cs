using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section:
///   Maxio:ApiKey                 - Maxio API key (used as the basic-auth username; password is "x")
///   Maxio:Subdomain              - Maxio site subdomain (used to derive https://{subdomain}.chargify.com)
///   Maxio:ProductFamilyHandle    - handle of the product family containing the subscription plans
///   Maxio:BaseUrl                - optional verbatim override for the API base address
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address instead
    /// of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
