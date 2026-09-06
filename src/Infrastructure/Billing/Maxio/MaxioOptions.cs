using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Nothing here has a baked-in value for a particular site or catalog: the same build runs
/// against any Maxio site by pointing these settings elsewhere.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Supplied through user-secrets / environment configuration, never the repo.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Maxio site, used to derive the API base address.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:Subdomain is required.")]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim and
    /// <see cref="Subdomain"/> / <see cref="Environment"/> are not used to derive one.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment used to derive the base address when <see cref="BaseUrl"/> is not
    /// set: <c>US</c> (default) or <c>EU</c>.
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.Us;

    /// <summary>Per-request timeout, in seconds.</summary>
    [Range(1, 120, ErrorMessage = "Maxio:TimeoutSeconds must be between 1 and 120.")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a request is retried after a throttled/transient failure. Maxio throttles on
    /// concurrency, so retries are few and backed off.
    /// </summary>
    [Range(0, 5, ErrorMessage = "Maxio:MaxRetries must be between 0 and 5.")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Optional override for how signups collect payment (<c>automatic</c>, <c>remittance</c>,
    /// <c>invoice</c>, <c>prepaid</c>). Left unset, the integration invoices rather than auto-charges,
    /// picking the value that suits the site's billing architecture - which is what lets a shopper
    /// subscribe without card capture.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>How long the plan catalog is cached in-process. Zero disables caching.</summary>
    [Range(0, 3600, ErrorMessage = "Maxio:CatalogCacheSeconds must be between 0 and 3600.")]
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Prefix applied to the Maxio customer <c>reference</c> derived from an eShopOnWeb user, so
    /// customers created by this app are recognisable on a shared site.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when supplied, otherwise the
    /// documented host for the configured environment with the site subdomain substituted in.
    /// A trailing slash is ensured so that any path in an override survives request composition.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}{MaxioEnvironments.HostSuffixFor(Environment)}"
            : BaseUrl!.Trim();

        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
