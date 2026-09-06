using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds the <c>Maxio:</c> configuration section. Values are supplied by configuration only — in
/// development via .NET user-secrets, in production via the platform's secret store. Nothing here
/// has a hard-coded credential or catalog default, so the same build runs against any Maxio site.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Site API key, used as the HTTP Basic username (password is the literal "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set it wins over <see cref="Subdomain"/>-derived
    /// addressing, which is what makes non-default hosts (proxies, alternate regions) reachable.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Prefix for the customer/subscription references this application owns in Maxio. Namespacing
    /// keeps eShopOnWeb records distinguishable when a site is shared with other applications.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// How Maxio should collect payment for subscriptions created here. eShopOnWeb captures no card
    /// details, so the default is remittance (invoice the customer) rather than automatic capture.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>How long the plan catalog and site metadata are cached before being re-read.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Per-request timeout applied to each individual call to Maxio.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Retry attempts for transient upstream failures (429 / 5xx / network), beyond the first try.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// The API base address: the explicit override when supplied, otherwise derived from the site
    /// subdomain. Always ends in a slash so relative request URIs compose correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var configured = BaseUrl!.Trim();
            if (!Uri.TryCreate(configured.EndsWith("/", StringComparison.Ordinal) ? configured : configured + "/",
                    UriKind.Absolute, out var explicitUri))
            {
                throw new InvalidOperationException(
                    $"'{SectionName}:{nameof(BaseUrl)}' is not a valid absolute URL: '{BaseUrl}'.");
            }

            return explicitUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Either '{SectionName}:{nameof(Subdomain)}' or '{SectionName}:{nameof(BaseUrl)}' must be configured.");
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/");
    }
}
