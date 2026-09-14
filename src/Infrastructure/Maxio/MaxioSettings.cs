using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Credentials are supplied out-of-band (user-secrets in development, the environment or a
/// secret store elsewhere) and are never committed.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the user name of HTTP Basic credentials, with "x" as the password
    /// (openapi.yaml, components.securitySchemes.BasicAuth).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Subdomain of the Maxio site. Substituted into the server template of the selected
    /// <see cref="Environment"/> (openapi.yaml, info.x-server-configuration).
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address. When set it is used verbatim and no URL is derived from
    /// <see cref="Subdomain"/> or <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment - "US" or "EU" - as enumerated by the specification's
    /// x-server-configuration. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.Default;

    /// <summary>
    /// Prefix applied to the reference this application stores on Maxio customers, so eShopOnWeb's
    /// records are recognisable on a site that other applications also write to.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshop-";

    /// <summary>
    /// Payment collection method applied to new subscriptions - one of the values of the
    /// specification's Collection-Method schema (<c>automatic</c>, <c>remittance</c>,
    /// <c>prepaid</c>, <c>invoice</c>); set to empty to let the Maxio site's own default apply.
    /// <para>
    /// The default is <c>remittance</c>: eShopOnWeb enrolls shoppers without capturing a payment
    /// method, and <c>automatic</c> collection fails at signup when no payment profile is on file.
    /// Change this to <c>automatic</c> once the storefront captures payment profiles.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; } = CollectionMethods.Remittance;

    /// <summary>Timeout applied to each individual HTTP attempt against Maxio.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retries after the first attempt for transient failures. Zero disables retrying.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the plan catalog is cached. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>Page size used when listing products. Maxio caps per_page at 200.</summary>
    public int PageSize { get; set; } = 200;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Clamp(RequestTimeoutSeconds, 1, 300));

    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(Math.Clamp(RetryBaseDelayMilliseconds, 0, 10_000));

    public TimeSpan PlanCacheDuration => TimeSpan.FromSeconds(Math.Max(PlanCacheSeconds, 0));

    public int EffectivePageSize => Math.Clamp(PageSize, 1, 200);

    /// <summary>The collection method to send, or null to omit the attribute entirely.</summary>
    public string? EffectivePaymentCollectionMethod =>
        string.IsNullOrWhiteSpace(PaymentCollectionMethod) ? null : PaymentCollectionMethod.Trim().ToLowerInvariant();

    /// <summary>True when everything needed to call Maxio is present.</summary>
    public bool IsConfigured => Validate().Count == 0;

    /// <summary>
    /// Returns a human-readable description of every configuration problem, naming the setting keys
    /// (never their values) so an operator can fix them.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{SectionName}:ApiKey' is missing");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{SectionName}:ProductFamilyHandle' is missing");
        }

        if (EffectivePaymentCollectionMethod is { } collectionMethod && !CollectionMethods.IsKnown(collectionMethod))
        {
            problems.Add($"'{SectionName}:PaymentCollectionMethod' must be empty or one of {CollectionMethods.KnownList}");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                problems.Add($"'{SectionName}:Subdomain' is missing (required unless '{SectionName}:BaseUrl' is set)");
            }
            else if (!MaxioEnvironments.IsSupported(Environment))
            {
                problems.Add($"'{SectionName}:Environment' must be one of {MaxioEnvironments.SupportedList}");
            }
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"'{SectionName}:BaseUrl' must be an absolute http(s) URL");
        }

        return problems;
    }
}
