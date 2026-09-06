using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Nothing here has a hard-coded default that ties the build to a particular Maxio site or
/// catalog: the API key, site subdomain and product family all come from configuration.
/// </summary>
public class MaxioOptions
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the basic-auth user name with the password <c>x</c>, as required by the
    /// <c>BasicAuth</c> security scheme in the Maxio OpenAPI specification.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Subdomain of the Maxio site. Substituted into the <c>https://{site}.chargify.com</c> server
    /// template declared by the specification.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address. When set it is used verbatim instead of deriving one from
    /// <see cref="Subdomain"/> - useful for the EU environment or for pointing tests at a stub.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional override for the Maxio <c>payment_collection_method</c> used at signup. When left
    /// unset the adapter picks the invoice-based method the site supports, because eShopOnWeb never
    /// captures card details and so cannot sign up under automatic collection.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retries after the initial attempt when Maxio returns a transient fault.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>How long the plan catalog and site metadata are cached, in seconds.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Base address to send requests to: <see cref="BaseUrl"/> when supplied, otherwise the
    /// specification's server template with <see cref="Subdomain"/> substituted in.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(EnsureTrailingSlash(BaseUrl!.Trim()), UriKind.Absolute, out var explicitUri))
            {
                throw new InvalidOperationException(
                    $"{ConfigurationSectionName}:{nameof(BaseUrl)} is not a valid absolute URL.");
            }

            return explicitUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"{ConfigurationSectionName}:{nameof(Subdomain)} is required when {nameof(BaseUrl)} is not set.");
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/");
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
