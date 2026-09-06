using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Everything the Maxio Advanced Billing integration needs, bound from the "Maxio" configuration
/// section. Values are supplied per environment (user-secrets locally, the platform's secret store in
/// production) and are never committed.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Site API key. Sent as the user name of HTTP Basic credentials, with "X" as the password, per
    /// the Billing API authentication guide.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site, e.g. "acme" for https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family holding the subscription plans this app sells.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of deriving
    /// one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional handle of the plan to enrol on when a subscribe request does not name one. Leaving it
    /// unset makes the plan handle mandatory on every subscribe request.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Per-request timeout. Kept below the 120s server-side cut-off documented for the Billing API.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a throttled or transient request is retried before giving up. The Billing API
    /// throttles on concurrency, so retries back off rather than fan out.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>How long the plan catalog is cached for. The catalog changes rarely.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>
    /// The API base address: <see cref="BaseUrl"/> verbatim when supplied, otherwise derived from the
    /// site subdomain. Always ends in "/" so relative request paths compose correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain!.Trim()}.chargify.com"
            : BaseUrl.Trim();

        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            throw new SubscriptionConfigurationException(
                $"'{ConfigurationSectionName}:{nameof(BaseUrl)}' is not a valid absolute URL.");
        }

        return uri;
    }

    /// <summary>
    /// Fails fast at start-up rather than letting a missing setting surface as a confusing 401 or 404
    /// on the first shopper request.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw Missing(nameof(ApiKey), "MAXIO_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw Missing(nameof(Subdomain), "MAXIO_SITE_SUBDOMAIN");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw Missing(nameof(ProductFamilyHandle), "MAXIO_DEFAULT_PRODUCT_FAMILY");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            throw new SubscriptionConfigurationException(
                $"'{ConfigurationSectionName}:{nameof(TimeoutSeconds)}' must be between 1 and 120.");
        }

        if (MaxRetryAttempts < 0)
        {
            throw new SubscriptionConfigurationException(
                $"'{ConfigurationSectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        // Surfaces a malformed BaseUrl override at start-up too.
        ResolveBaseAddress();
    }

    private static SubscriptionConfigurationException Missing(string key, string environmentVariable) =>
        new($"'{ConfigurationSectionName}:{key}' is not configured. Set it from the {environmentVariable} " +
            "environment variable, for example with 'dotnet user-secrets set'.");
}
