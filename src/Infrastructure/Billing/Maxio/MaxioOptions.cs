using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is a secret and must come from user-secrets, environment variables or a vault —
/// never from a file committed to the repository.
/// </remarks>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Advanced Billing API key. Sent as the user name of HTTP basic auth, with <c>x</c> as password.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Advanced Billing site, used to derive the API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override of the API base address. When set it is used verbatim and
    /// <see cref="Subdomain"/> and <see cref="Environment"/> are not consulted.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Hosting region of the Advanced Billing site: <c>US</c> (default) or <c>EU</c>.
    /// Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public MaxioEnvironment Environment { get; set; } = MaxioEnvironment.US;

    /// <summary>
    /// How the provider should collect payment for subscriptions created by this integration.
    /// <c>remittance</c> (invoice the customer) lets a shopper subscribe without a stored card;
    /// <c>automatic</c> charges a payment method, which this integration does not capture.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Prefix applied to every customer and subscription reference this integration creates.</summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>HTTP timeout for a single call to the provider.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retries after a transient provider failure (429 / 5xx / network error).</summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay of the exponential backoff between retries, in milliseconds.</summary>
    [Range(0, 60_000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the plan catalog is cached in memory. Zero disables caching.</summary>
    [Range(0, 3600)]
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Resolves the address every API call is built on: <see cref="BaseUrl"/> verbatim when supplied,
    /// otherwise the regional host for <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl, UriKind.Absolute);
        }

        // Hosts as published by Maxio for the Advanced Billing API.
        var host = Environment == MaxioEnvironment.EU
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";

        return new Uri(host, UriKind.Absolute);
    }

    internal void Validate()
    {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new ValidationException("Maxio:Subdomain is required unless Maxio:BaseUrl is set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new ValidationException($"Maxio:BaseUrl must be an absolute URL, but was '{BaseUrl}'.");
        }
    }
}

/// <summary>Advanced Billing hosting regions.</summary>
public enum MaxioEnvironment
{
    US = 0,
    EU = 1
}
