using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the <c>Maxio:</c> section. The credential and site values
/// are supplied by configuration (user-secrets / environment) and never hard-coded, so the same build runs
/// against a different Maxio site and catalog. <see cref="ApiKey"/> and <see cref="Subdomain"/> are required
/// and validated at startup (see <c>AddMaxioBilling</c>): a blank credential is a deployment fault that must
/// stop the host, not a 401 discovered on the first call.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Chargify API key (bound from <c>Maxio:ApiKey</c>, sourced from <c>MAXIO_API_KEY</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (bound from <c>Maxio:Subdomain</c>, sourced from <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are the subscribable plans (bound from <c>Maxio:ProductFamilyHandle</c>, sourced from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL (bound from <c>Maxio:BaseUrl</c>). When set it is used verbatim as the
    /// Production base address instead of deriving <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used when a subscribe request omits a plan. When unset, the first plan
    /// returned by the product family is used — so nothing about the catalog is hard-coded.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions (Maxio <c>payment_collection_method</c>). Defaults to
    /// <c>remittance</c> so a subscription can be created and billed by invoice without capturing a card —
    /// the plans in scope do not require a payment method. Override (e.g. <c>invoice</c> on legacy Statements
    /// sites, or <c>automatic</c> where a card is captured elsewhere) via <c>Maxio:PaymentCollectionMethod</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Total per-operation timeout budget, in seconds, enforced by the billing service. Defaults to 30.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
