using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// Values are supplied by the environment (via user-secrets / environment variables) and are never
/// committed to the repository. See the deployment guide for the env-var → key mapping.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key. Basic-auth username; the password is the constant <c>x</c>. From <c>MAXIO_API_KEY</c>.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain — substituted into the Production base URL. From <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are the subscribable plans. From <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim as the Production/US base
    /// address instead of deriving <c>https://{subdomain}.chargify.com</c> from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used when a subscribe request names no plan. When unset, the
    /// service falls back to the first plan in the product family, keeping the build catalog-agnostic.
    /// </summary>
    public string? DefaultProductHandle { get; set; }
}
