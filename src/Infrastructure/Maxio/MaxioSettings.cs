using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is a secret and must come from user-secrets, environment variables
/// (<c>Maxio__ApiKey</c>) or a vault - never from a file in the repository.
/// </remarks>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the HTTP Basic user name, with the literal password <c>x</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Maxio site, e.g. the <c>acme</c> in <c>https://acme.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim; otherwise the
    /// address is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// EU-hosted sites should set this to <c>https://{subdomain}.ebilling.maxio.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the plan used when a subscribe request does not name one. Left unset by default so
    /// that the application never guesses which plan to charge a shopper for.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Collection method applied to new subscriptions. <c>remittance</c> (invoice the customer)
    /// is the default because eShopOnWeb captures no payment details; <c>automatic</c> would make
    /// Maxio attempt to charge a stored payment profile at signup and fail without one.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = MaxioCollectionMethods.Remittance;

    /// <summary>
    /// Prefix for the Maxio customer reference derived from an eShopOnWeb user. Change it when two
    /// applications share one Maxio site so their customer references cannot collide.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>How long the plan catalog and site currency are cached. Set to 0 to disable caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>Per-request timeout for calls to Maxio.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Total attempts (initial try plus retries) for a retryable Maxio call.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// The base address to send API requests to, with the trailing slash <see cref="Uri"/>
    /// composition requires.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var address = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}.chargify.com"
            : BaseUrl!.Trim();

        return new Uri(address.EndsWith('/') ? address : address + "/", UriKind.Absolute);
    }
}
