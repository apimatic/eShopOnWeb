using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Nothing here has a hard-coded credential or catalog value: the same build has to
/// run against a different Maxio site and a different catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username with the literal password "X",
    /// per the Billing API authentication docs.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of
    /// deriving one from <see cref="Subdomain"/> (e.g. an EU-hosted site or a test double).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request timeout. Maxio itself cuts requests off at 120 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Retry attempts after the first try, for throttled and transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>
    /// Client-side ceiling on in-flight calls. Maxio runs at most 4 concurrent workers per
    /// subdomain and queues the rest, so staying at or below that avoids self-inflicted throttling.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>How long the plan catalog is cached. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>How long the site's own settings (currency, invoicing model) are cached.</summary>
    public int SiteCacheSeconds { get; set; } = 300;

    /// <summary>
    /// Optional override for the collection method used when creating subscriptions
    /// ("remittance", "invoice", "automatic", "prepaid"). Left unset, one is chosen from the
    /// plan and the site: plans that need no payment method are billed by invoice, because
    /// eShopOnWeb captures no card details and an automatic signup would fail on the first charge.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Prefix for the customer/subscription references this application owns in Maxio.</summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// Resolves the base address for the Maxio API, honouring <see cref="BaseUrl"/> when supplied.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var configured = BaseUrl.Trim();

            if (!Uri.TryCreate(configured, UriKind.Absolute, out var absolute))
            {
                throw new BillingConfigurationException(
                    $"{SectionName}:BaseUrl must be an absolute URL when set.");
            }

            // A trailing slash is required for relative request URIs to resolve against the
            // configured address rather than replacing its last segment.
            return configured.EndsWith('/') ? absolute : new Uri(configured + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"{SectionName}:Subdomain is required unless {SectionName}:BaseUrl is set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/");
    }

    /// <summary>
    /// Throws when the integration cannot run with the current settings.
    /// </summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                $"{SectionName}:ApiKey is not configured. Load it from the MAXIO_API_KEY environment " +
                "variable into user-secrets or another secret store - never into a file in the repository.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                $"{SectionName}:ProductFamilyHandle is not configured (MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        _ = ResolveBaseAddress();
    }
}
