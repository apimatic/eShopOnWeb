using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section.
///
/// Nothing here has a baked-in value for a particular site or catalog: the same build must run
/// against a different Maxio site with a different product family. Credentials come from
/// user-secrets (locally) or the environment/secret store (elsewhere) and are never committed.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Site API key. Sent as the HTTP Basic username with the literal password "x", per the
    /// Advanced Billing authentication scheme.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Advanced Billing site subdomain (e.g. the "acme" in acme.chargify.com).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute base address. When set it is used verbatim and <see cref="Subdomain"/> is
    /// not used to derive one - useful for a proxy, a private host, or a record/replay test double.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are published as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// "US" (default) or "EU". Selects the hosting region used to derive the base address from
    /// <see cref="Subdomain"/>. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.US;

    /// <summary>
    /// Optional override for the payment collection method used at signup. When left unset the
    /// integration picks the correct value for the site's invoicing architecture: "remittance" for
    /// Relationship Invoicing sites, "invoice" for legacy Statements sites. Either way the shopper
    /// is invoiced rather than charged, which is what lets a plan with no stored payment method
    /// activate immediately.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Unset by default, so
    /// that a missing plan handle is reported as a caller error rather than silently guessed.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>How long the published plan catalog and site metadata are cached. Zero disables caching.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Total time budget for one billing operation, retries included. HttpClient folds this into
    /// the cancellation token it passes down, so it bounds the whole attempt chain rather than
    /// each attempt separately.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a retry-safe call is retried on a transient failure (429, 5xx, network
    /// error). Non-idempotent calls are only retried when the billing system says it did not
    /// process the request (429).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential, jittered retry backoff.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Prefix applied to every customer and subscription reference this integration writes, so
    /// records created by eShopOnWeb are recognisable in the Maxio UI and never collide with
    /// references created by another system on the same site.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshop";

    /// <summary>Resolves the API base address, honouring <see cref="BaseUrl"/> when supplied.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            // Used verbatim. The trailing slash matters: relative request paths are combined
            // against it, so without one the last path segment would be dropped.
            var verbatim = BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/";
            return new Uri(verbatim, UriKind.Absolute);
        }

        var host = MaxioEnvironments.IsEu(Environment)
            ? $"https://{Subdomain}.ebilling.maxio.com/"
            : $"https://{Subdomain}.chargify.com/";

        return new Uri(host, UriKind.Absolute);
    }
}

public static class MaxioEnvironments
{
    public const string US = "US";
    public const string EU = "EU";

    public static bool IsEu(string? environment) =>
        string.Equals(environment, EU, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? environment) =>
        string.IsNullOrWhiteSpace(environment)
        || string.Equals(environment, US, StringComparison.OrdinalIgnoreCase)
        || IsEu(environment);
}
