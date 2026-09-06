namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Everything the Maxio Advanced Billing integration reads from configuration, bound from the
/// <c>Maxio</c> section. No value here has a hard-coded fallback that points at a particular Maxio
/// site or catalog: the same build has to run against a different site and a different plan set.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key. Supplied as the basic-auth user name by the SDK.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, used to derive the API host when no explicit base URL is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim, in place of the
    /// host derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional Maxio region, <c>US</c> (default) or <c>EU</c>. Selects which per-environment server
    /// options <see cref="Subdomain"/> and <see cref="BaseUrl"/> are applied to.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Left unset, a request
    /// without a plan handle is rejected rather than silently enrolling the shopper in some plan.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Optional override for how a new subscription's balance is collected: <c>remittance</c>,
    /// <c>invoice</c>, <c>automatic</c> or <c>prepaid</c>. Left unset, the right member is read from
    /// the Maxio site's billing architecture, which is the safe default — the correct value differs
    /// between Relationship Invoicing and legacy Statements sites.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Whole-call budget for one provider operation, across every retry attempt.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Bound on a single HTTP attempt. Several may occur inside one call budget.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>How long a resolved product-family id is reused before being looked up again.</summary>
    public int CatalogCacheMinutes { get; set; } = 10;

    /// <summary>Page size used when walking the product family's plan list.</summary>
    public int PlanPageSize { get; set; } = 50;

    /// <summary>Upper bound on plan-list pages, so a provider that never returns a short page cannot loop forever.</summary>
    public int MaxPlanPages { get; set; } = 20;

    /// <summary>
    /// True when enough is configured to talk to Maxio at all: a key, a catalog, and either a
    /// subdomain or an explicit base URL.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
