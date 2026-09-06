using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. No value here is ever hard-coded: the same build has to run against a different Maxio site
/// and a different catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The subdomain placeholder the SDK ships as its default. Treated as "not configured".</summary>
    internal const string UnconfiguredSubdomain = "subdomain";

    /// <summary>Maxio API key. Sent as the HTTP Basic username. Bound from <c>Maxio:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. <c>cp-exp-1</c>. Bound from <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family holding the subscribable plans. Bound from <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set it is used as-is instead of deriving one from
    /// <see cref="Subdomain"/>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio server region: <c>US</c> (default) or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>Total budget for one logical billing operation, including any retries. Bounds what the caller waits for.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Budget for a single HTTP attempt. Must leave room for <see cref="MaxRetries"/> attempts inside <see cref="RequestTimeout"/>.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Extra attempts after the first. The SDK's retry pipeline enforces a floor of 1.</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>How long the resolved product-family id, the site currency and the plan list are cached.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Namespace prefix for the Maxio customer <c>reference</c> we derive from an eShopOnWeb identity, so a
    /// shared Maxio site can host more than one application without reference collisions.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// Optional override for the payment-collection method used when creating a subscription
    /// (<c>remittance</c>, <c>invoice</c>, <c>automatic</c>, <c>prepaid</c>). Left unset, it is derived
    /// from the site's invoicing architecture, which is the safe default for an API that captures no card.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Logs the outbound method/URL and response status of every Maxio call. Off by default.</summary>
    public bool LogRequests { get; set; }

    public bool IsEuropeanRegion =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the first configuration problem that would make Maxio calls fail, or <c>null</c> when the
    /// section is usable. Checked per operation rather than at startup so that a missing Maxio
    /// configuration disables only the subscription endpoints, not the whole application.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return $"{SectionName}:{nameof(ApiKey)} is not configured.";
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            return $"{SectionName}:{nameof(ProductFamilyHandle)} is not configured.";
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(BaseUrl);
        var hasSubdomain = !string.IsNullOrWhiteSpace(Subdomain)
            && !string.Equals(Subdomain!.Trim(), UnconfiguredSubdomain, StringComparison.OrdinalIgnoreCase);

        if (!hasBaseUrl && !hasSubdomain)
        {
            return $"{SectionName}:{nameof(Subdomain)} is not configured (and no {SectionName}:{nameof(BaseUrl)} override was supplied).";
        }

        if (hasBaseUrl && !Uri.IsWellFormedUriString(BaseUrl!.Trim(), UriKind.Absolute))
        {
            return $"{SectionName}:{nameof(BaseUrl)} is not an absolute URL.";
        }

        return null;
    }
}
