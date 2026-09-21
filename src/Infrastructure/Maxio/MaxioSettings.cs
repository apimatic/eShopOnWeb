using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section. Values are
/// supplied per deployment (never hard-coded); secrets come from user-secrets / environment, not the repo.
/// Validated with <c>ValidateDataAnnotations().ValidateOnStart()</c> so a missing/blank required value stops
/// the host at startup rather than surfacing as a 401 on the first call.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Chargify API key (basic-auth username; password is the literal <c>x</c>). From <c>MAXIO_API_KEY</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (the <c>{site}</c> in <c>https://{site}.chargify.com</c>). From <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are the subscribable plans. From <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim as the Production/US base address instead
    /// of being derived from <see cref="Subdomain"/>. Leave blank to derive from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used when a subscribe request omits one. Catalog-specific, so it is
    /// configuration (never hard-coded). When unset, the first plan the family returns is used.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Payment collection method for created subscriptions. Defaults to <c>remittance</c> (invoice billing,
    /// no card capture), which suits plans that do not require a payment method — the default collection
    /// (<c>automatic</c>) would instead attempt an immediate charge and fail without a card. Site-architecture
    /// specific: <c>remittance</c> on Relationship Invoicing sites, <c>invoice</c> on legacy Statements sites.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
