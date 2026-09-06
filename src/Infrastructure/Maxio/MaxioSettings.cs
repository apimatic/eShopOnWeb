using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds the "Maxio" configuration section.
/// <para>
/// <see cref="ApiKey"/> is a secret and must come from an out-of-repository source
/// (user-secrets in development, a key vault or environment in production). Nothing in this class
/// carries a default that points at a specific Maxio site or catalog.
/// </para>
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key. Sent as the HTTP Basic username, with "X" as the password.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim (this is how you
    /// target the EU environment, a gateway, or a test double); otherwise the address is derived
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Left unset by default so
    /// that the build carries no assumption about the contents of any particular catalog.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How renewals are collected for subscriptions this app creates: "automatic", "remittance",
    /// "invoice" or "prepaid".
    /// <para>
    /// Left unset by default, in which case the value is derived from the site: eShopOnWeb never
    /// captures a card, so an automatically-collected subscription could not complete its signup
    /// charge. Invoice-based collection ("remittance" on Relationship Invoicing sites, "invoice" on
    /// statement-based ones) is what lets a shopper subscribe without card capture or 3-DS. Set this
    /// explicitly to "automatic" on a deployment that does capture payment methods.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Width of the time bucket used to derive a uniqueness_token when the caller supplies no
    /// idempotency key. Two subscribe attempts for the same shopper and plan inside one bucket are
    /// treated by Maxio as the same attempt; a later retry starts a new one.
    /// </summary>
    public int IdempotencyWindowSeconds { get; set; } = 60;

    /// <summary>How long the site's architecture is cached. It effectively never changes.</summary>
    public int SiteCacheSeconds { get; set; } = 3600;

    /// <summary>Per-request timeout. Maxio itself cuts requests off at 120s.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retries after the initial attempt, for throttled/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Maximum in-flight requests to Maxio from this process. Maxio limits a site to 4 concurrent
    /// API calls and queues the excess, so we shape the load rather than letting it be throttled.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>How long the plan catalog is cached. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Prefix for the Maxio customer "reference" that links a billing customer to an eShopOnWeb account.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// The API base address: <see cref="BaseUrl"/> verbatim when supplied, otherwise derived from
    /// <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}.chargify.com"
            : BaseUrl!.Trim();

        // HttpClient resolves relative request URIs against the base address, which requires a
        // trailing slash or the last path segment is discarded.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
