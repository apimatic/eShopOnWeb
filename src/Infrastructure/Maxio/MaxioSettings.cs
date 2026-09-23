namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// Values are supplied by configuration (user-secrets / environment) and never hard-coded, so the same
/// build runs against any Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key — the Basic-auth username. From <c>Maxio:ApiKey</c> (env <c>MAXIO_API_KEY</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. <c>cp-exp-1</c>. From <c>Maxio:Subdomain</c> (env <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the subscribable plans. From <c>Maxio:ProductFamilyHandle</c> (env <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim instead of deriving
    /// <c>https://{subdomain}.chargify.com</c> from <see cref="Subdomain"/>. From <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used when a subscribe request omits one. From <c>Maxio:DefaultPlanHandle</c>.
    /// Not one of the mandated keys; when unset, a subscribe request must name a plan.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Optional payment collection method for new subscriptions. From <c>Maxio:PaymentCollectionMethod</c>.
    /// Defaults to <c>remittance</c> so priced plans can be subscribed without a card on file (invoice
    /// billing). Set to <c>invoice</c> on legacy Statements-Architecture sites, or <c>automatic</c> if a
    /// stored payment profile is expected.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
