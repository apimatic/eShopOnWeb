using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for talking to Maxio Advanced Billing. Bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here has a hard-coded site, catalog or credential: the same build runs against any Maxio
/// site by changing configuration only. Supply values through user-secrets, environment variables
/// (<c>Maxio__ApiKey</c>, ...) or any other configuration provider - never through a file in the repo.
/// </para>
/// <para>
/// The base address follows the server list in the OpenAPI specification's
/// <c>x-server-configuration</c> block: <c>https://{site}.chargify.com</c> for the US environment and
/// <c>https://{site}.ebilling.maxio.com</c> for EU.
/// </para>
/// </remarks>
public class MaxioOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Used as the username of the HTTP Basic credential, with the literal password
    /// <c>x</c>, exactly as the spec's <c>BasicAuth</c> security scheme describes.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site, substituted into the server URL template.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim and neither
    /// <see cref="Subdomain"/> nor <see cref="Environment"/> takes part in building the base address.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment, <c>US</c> (default) or <c>EU</c>. Selects which server template from
    /// the specification is used when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Environment { get; set; } = UsEnvironment;

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Left unset, callers must
    /// always pass a plan handle.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Prefix for the provider-side references this application owns, so eShopOnWeb's customers and
    /// subscriptions are recognisable on a site that other systems may also write to.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshop";

    /// <summary>
    /// How new subscriptions are collected, one of the values in the specification's
    /// <c>Collection-Method</c> enum: <c>automatic</c>, <c>remittance</c>, <c>prepaid</c> or
    /// <c>invoice</c>.
    /// <para>
    /// Left unset, the collection method is chosen from the site's own architecture:
    /// <c>remittance</c> on Relationship Invoicing sites, <c>invoice</c> on legacy Statements sites.
    /// Both mean "issue an invoice" - which is the only thing eShopOnWeb can honestly promise, since
    /// it captures no card details. Set this to <c>automatic</c> only on a deployment that does
    /// capture payment methods.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a retryable request is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>How long the site's currency is cached for, in seconds.</summary>
    public int SiteCacheSeconds { get; set; } = 3600;

    public const string UsEnvironment = "US";
    public const string EuEnvironment = "EU";

    /// <summary>The <c>Collection-Method</c> enum from the specification.</summary>
    public static readonly string[] CollectionMethods = { "automatic", "remittance", "prepaid", "invoice" };

    /// <summary>Server URL templates, keyed by environment name, taken from the specification.</summary>
    private static readonly Dictionary<string, string> ServerTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        [UsEnvironment] = "https://{site}.chargify.com",
        [EuEnvironment] = "https://{site}.ebilling.maxio.com"
    };

    /// <summary>
    /// Resolves the API base address: the <see cref="BaseUrl"/> override when present, otherwise the
    /// environment's server template with <see cref="Subdomain"/> substituted for <c>{site}</c>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitAddress = BaseUrl!.Trim();
            if (!Uri.TryCreate(EnsureTrailingSlash(explicitAddress), UriKind.Absolute, out var parsed))
            {
                throw new InvalidOperationException($"'{SectionName}:{nameof(BaseUrl)}' is not a valid absolute URL.");
            }

            return parsed;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (!ServerTemplates.TryGetValue(Environment ?? UsEnvironment, out var template))
        {
            throw new InvalidOperationException($"'{SectionName}:{nameof(Environment)}' must be one of: {string.Join(", ", ServerTemplates.Keys)}.");
        }

        return new Uri(EnsureTrailingSlash(template.Replace("{site}", Subdomain!.Trim(), StringComparison.Ordinal)));
    }

    /// <summary>Returns the configuration problems that stop this capability from working, if any.</summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            failures.Add($"'{SectionName}:{nameof(ApiKey)}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            failures.Add($"Either '{SectionName}:{nameof(Subdomain)}' or '{SectionName}:{nameof(BaseUrl)}' must be configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            failures.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Environment) && !ServerTemplates.ContainsKey(Environment))
        {
            failures.Add($"'{SectionName}:{nameof(Environment)}' must be one of: {string.Join(", ", ServerTemplates.Keys)}.");
        }

        if (TimeoutSeconds <= 0)
        {
            failures.Add($"'{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            failures.Add($"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod) &&
            Array.IndexOf(CollectionMethods, PaymentCollectionMethod!.Trim().ToLowerInvariant()) < 0)
        {
            failures.Add($"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of: {string.Join(", ", CollectionMethods)}.");
        }

        return failures;
    }

    /// <summary>True when the capability has everything it needs to call the provider.</summary>
    public bool IsConfigured => Validate().Count == 0;

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
