using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section.
/// </summary>
/// <remarks>
/// Nothing here has a baked-in value that ties the build to one Maxio site or one catalog: the site
/// (<see cref="Subdomain"/>), the credential (<see cref="ApiKey"/>) and the catalog
/// (<see cref="ProductFamilyHandle"/>) are all supplied by configuration. Deployments that do not
/// live on the default US host override <see cref="BaseUrl"/> outright.
/// </remarks>
public class MaxioOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username with the literal password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the specification.
    /// </summary>
    /// <remarks>Supply through user secrets, a key vault or the environment. Never through a file in the repository.</remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Subdomain of the Maxio site, substituted into the <c>site</c> server variable of the
    /// specification server template. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family whose products are offered as subscription plans.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim as the API base
    /// address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Hosting environment of the Maxio site, as named by the <c>x-server-configuration</c>
    /// block of the specification. Only used when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Environment { get; set; } = UsEnvironment;

    /// <summary>
    /// Collection method requested for new subscriptions. Must be one of the values of the
    /// specification <c>Collection Method</c> enumeration.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>remittance</c>, which invoices the customer rather than charging a stored
    /// card. That is the collection method that matches plans configured with
    /// "payment method not required": with <c>automatic</c>, Maxio rejects the signup with
    /// "No payment method was on file" because eShopOnWeb captures no card. Sites that do capture
    /// payment profiles should set this to <c>automatic</c>.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Prefix for the customer and subscription references eShopOnWeb assigns in Maxio. Give each
    /// deployment its own prefix when several of them share a single Maxio site.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>How long a single HTTP call to Maxio may take before it is abandoned.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a failed call is retried. Retries apply to reads, to rate limiting and to
    /// connection failures only, never to a write that the provider may already have accepted.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>
    /// How long the plan catalog and the site record are cached. Both change rarely and are read on
    /// every plan listing and every subscribe request.
    /// </summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    public const string UsEnvironment = "US";
    public const string EuEnvironment = "EU";

    /// <summary>Collection methods accepted by the specification <c>Collection Method</c> schema.</summary>
    internal static readonly string[] SupportedCollectionMethods =
        { "automatic", "remittance", "prepaid", "invoice" };

    /// <summary><c>true</c> when enough is configured to attempt a call to Maxio.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain))
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>
    /// Resolves the base address for the Maxio API.
    /// </summary>
    /// <remarks>
    /// A trailing slash is guaranteed so that relative request paths resolve underneath any path
    /// segments the override may carry; the scheme, host and path of an explicit
    /// <see cref="BaseUrl"/> are otherwise left untouched.
    /// </remarks>
    public Uri ResolveBaseAddress()
    {
        var errors = Validate().ToList();
        if (errors.Count > 0)
        {
            throw new BillingConfigurationException(
                $"Subscription billing is not configured: {string.Join(" ", errors)}");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            return new Uri(trimmed.EndsWith('/') ? trimmed : trimmed + "/", UriKind.Absolute);
        }

        // Server templates come from the specification: the default US production server is
        // https://{site}.chargify.com and EU-hosted sites live at https://{site}.ebilling.maxio.com.
        var host = IsEuEnvironment
            ? $"https://{Subdomain!.Trim()}.ebilling.maxio.com/"
            : $"https://{Subdomain!.Trim()}.chargify.com/";

        return new Uri(host, UriKind.Absolute);
    }

    internal bool IsEuEnvironment =>
        string.Equals(Environment, EuEnvironment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a human readable problem for every setting that would stop the integration working.
    /// </summary>
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return $"'{SectionName}:{nameof(ApiKey)}' is required.";
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            yield return $"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.";
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl)
            && !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            yield return $"'{SectionName}:{nameof(BaseUrl)}' must be an absolute URL.";
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            yield return $"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.";
        }

        if (!string.Equals(Environment, UsEnvironment, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Environment, EuEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"'{SectionName}:{nameof(Environment)}' must be either '{UsEnvironment}' or '{EuEnvironment}'.";
        }

        if (!SupportedCollectionMethods.Contains(PaymentCollectionMethod, StringComparer.OrdinalIgnoreCase))
        {
            yield return $"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of: {string.Join(", ", SupportedCollectionMethods)}.";
        }

        if (TimeoutSeconds <= 0)
        {
            yield return $"'{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.";
        }

        if (MaxRetryAttempts < 0)
        {
            yield return $"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.";
        }
    }
}
