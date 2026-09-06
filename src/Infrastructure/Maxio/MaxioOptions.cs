using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied by configuration (user-secrets / environment) only - nothing here
/// is hard-coded, so the same build runs against any Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the basic-auth user name with the password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the OpenAPI specification.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain. Fills the <c>site</c> server variable of the specification's
    /// server template when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family whose products are offered as subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override of the API base address. When set it is used exactly as given
    /// and neither <see cref="Subdomain"/> nor <see cref="Environment"/> affect the base address.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment, from the specification's <c>x-server-configuration</c>:
    /// <c>US</c> maps to <c>https://{site}.chargify.com</c>,
    /// <c>EU</c> maps to <c>https://{site}.ebilling.maxio.com</c>.
    /// Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = UsEnvironment;

    /// <summary>
    /// Maxio <c>Collection-Method</c> used when enrolling a shopper: one of <c>automatic</c>,
    /// <c>remittance</c>, <c>prepaid</c> or <c>invoice</c>.
    /// </summary>
    /// <remarks>
    /// This integration does not capture card details, so it never attaches a payment profile.
    /// <c>automatic</c> collection would therefore fail at signup with "no payment method was on
    /// file", which is why an invoice-based method is the default. Set this to <c>automatic</c>
    /// only on a deployment that provisions payment profiles by some other means.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Valid <c>Collection-Method</c> values, from the OpenAPI specification.</summary>
    public static readonly string[] CollectionMethods = { "automatic", "remittance", "prepaid", "invoice" };

    /// <summary>Per-request timeout for calls to Maxio.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a retryable Maxio call is re-attempted before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential back-off between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the plan catalogue is cached in-process. Set to zero to disable caching.</summary>
    public TimeSpan PlanCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a resolved billing customer is cached in-process. Set to zero to disable caching.
    /// Kept short so a customer removed in Maxio is re-resolved rather than pinned forever.
    /// </summary>
    public TimeSpan CustomerCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Prefix for the Maxio customer <c>reference</c> derived from an eShopOnWeb user, so that
    /// references written by this application are distinguishable on a shared Maxio site.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    public const string UsEnvironment = "US";
    public const string EuEnvironment = "EU";

    private static readonly Dictionary<string, string> EnvironmentHostTemplates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [UsEnvironment] = "https://{0}.chargify.com",
            [EuEnvironment] = "https://{0}.ebilling.maxio.com"
        };

    public static bool IsKnownEnvironment(string? environment) =>
        !string.IsNullOrWhiteSpace(environment) && EnvironmentHostTemplates.ContainsKey(environment);

    /// <summary>
    /// Resolves the API base address: the verbatim <see cref="BaseUrl"/> when supplied, otherwise
    /// the specification's server template for <see cref="Environment"/> filled with <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var address = string.IsNullOrWhiteSpace(BaseUrl)
            ? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                EnvironmentHostTemplates.TryGetValue(Environment ?? string.Empty, out var template)
                    ? template
                    : EnvironmentHostTemplates[UsEnvironment],
                Subdomain)
            : BaseUrl.Trim();

        // A trailing slash keeps the last path segment when relative request URIs are combined.
        if (!address.EndsWith('/'))
        {
            address += "/";
        }

        return new Uri(address, UriKind.Absolute);
    }
}
