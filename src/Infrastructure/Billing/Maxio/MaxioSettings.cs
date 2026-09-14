using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ApiKey"/> is a secret and must come from a secret store -- .NET user-secrets in development,
/// environment variables or a key vault elsewhere. It is never committed to the repository.
/// </para>
/// <para>
/// Nothing here has a site- or catalog-specific default: the same build runs against any Maxio site and
/// any product family purely by changing configuration.
/// </para>
/// </remarks>
public class MaxioSettings
{
    /// <summary>Name of the configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Host template used when <see cref="BaseUrl"/> is not supplied. Maxio serves the US region from chargify.com.</summary>
    private const string DerivedHostFormat = "https://{0}.chargify.com/";

    /// <summary>Maxio API key. Sent as the HTTP Basic user name, with a literal "x" as the password.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site, e.g. the "acme" in acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are published as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim, and
    /// <see cref="Subdomain"/> is not used to derive an address. Useful for EU-hosted sites
    /// (https://{site}.ebilling.maxio.com) or for pointing tests at a stub.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the plan used when a subscribe request does not name one. When left unset and the
    /// product family offers exactly one plan, that plan is the default.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio should collect payment for subscriptions created here. Defaults to <c>remittance</c>
    /// (invoice the customer) because this integration deliberately captures no card details: an
    /// <c>automatic</c> signup is rejected by Maxio when a balance is due and no payment method is on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>How long the plan catalog and site currency are cached before being re-read from Maxio.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Per-request timeout for calls to Maxio.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a failed call to Maxio is retried. Set to zero to disable retries.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Prefix applied to the customer and subscription references this application writes into Maxio,
    /// so its records are distinguishable from those of any other system sharing the same site.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// Resolves the address the API client talks to: <see cref="BaseUrl"/> verbatim when supplied,
    /// otherwise derived from <see cref="Subdomain"/>.
    /// </summary>
    /// <remarks>
    /// A trailing slash is appended when absent. <see cref="System.Net.Http.HttpClient.BaseAddress"/>
    /// resolves relative request paths against the address, and without the trailing slash the last
    /// segment of an override such as https://gateway.internal/maxio would be dropped.
    /// </remarks>
    /// <exception cref="BillingConfigurationException">Neither a usable override nor a usable subdomain was configured.</exception>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var configured = BaseUrl.Trim();
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var overrideAddress) ||
                (overrideAddress.Scheme != Uri.UriSchemeHttps && overrideAddress.Scheme != Uri.UriSchemeHttp))
            {
                throw new BillingConfigurationException(
                    $"'{MaxioSettings.SectionName}:{nameof(BaseUrl)}' must be an absolute http or https URL, but was '{configured}'.");
            }

            return WithTrailingSlash(overrideAddress);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(Subdomain)}' is required unless '{MaxioSettings.SectionName}:{nameof(BaseUrl)}' is set.");
        }

        var subdomain = Subdomain.Trim();
        if (subdomain.IndexOfAny(new[] { '/', ':', ' ' }) >= 0)
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(Subdomain)}' must be a bare subdomain such as 'acme', not a URL, but was '{subdomain}'.");
        }

        return new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, DerivedHostFormat, subdomain));
    }

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? address : new Uri(address.AbsoluteUri + "/");
}
