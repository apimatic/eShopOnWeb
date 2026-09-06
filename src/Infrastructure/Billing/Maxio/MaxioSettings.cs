using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Nothing here has a hard-coded value: the same build
/// has to run against a different Maxio site and a different catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Site API key. Sent as the HTTP Basic user name (password is the literal "x"). Supply through
    /// user-secrets in development, or the Maxio__ApiKey environment variable elsewhere - never in
    /// a checked-in settings file.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:Subdomain is required.")]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute override for the API base address. When set it is used verbatim; otherwise
    /// the address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How new subscriptions collect their balance: "remittance" (invoice the customer) or
    /// "automatic" (charge a stored payment method). Defaults to remittance because eShopOnWeb
    /// captures no card - "automatic" signup fails unless a payment profile already exists.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Prefix for the customer reference derived from the eShopOnWeb user name, so eShopOnWeb
    /// customers never collide with other applications sharing the same Maxio site.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb:";

    /// <summary>Per-attempt timeout for a single call to Maxio.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Extra attempts after the first for retryable failures (429 / 5xx / transport).</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>How long the site's default currency is cached. It effectively never changes.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan SiteCacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Resolves the API base address, honouring <see cref="BaseUrl"/> when supplied.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configured))
            {
                throw new InvalidOperationException($"Maxio:BaseUrl '{BaseUrl}' is not an absolute URI.");
            }

            return EnsureTrailingSlash(configured);
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }

    // HttpClient only treats a BaseAddress as a prefix when it ends in "/".
    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/");
}
