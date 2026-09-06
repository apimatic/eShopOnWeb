using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> section.
/// </summary>
/// <remarks>
/// Nothing here has a baked-in default that points at a particular Maxio site or catalog: the same
/// build has to run against any site. Supply the values through user-secrets, environment variables
/// (<c>Maxio__ApiKey</c>, <c>Maxio__Subdomain</c>, ...) or any other configuration provider. Never
/// commit them.
/// </remarks>
public class MaxioSettings
{
    /// <summary>Name of the configuration section these settings are bound from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic user name, with the fixed password <c>x</c>, which is
    /// the authentication scheme Advanced Billing documents.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site, for example <c>acme</c> in <c>acme.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override of the API base address. When set it is used verbatim; otherwise the address
    /// is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>. Set it
    /// explicitly for EU-hosted sites, whose address is <c>https://{subdomain}.ebilling.maxio.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How Maxio should collect payment for subscriptions this application creates. <c>remittance</c>
    /// (Relationship Invoicing) or <c>invoice</c> (legacy Statements) let a shopper subscribe without
    /// a stored payment method by issuing an invoice; <c>automatic</c> charges a stored payment
    /// method and therefore requires one to exist.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-request timeout for calls to Maxio.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a transient failure (429, 5xx, connection fault) is retried before giving up.
    /// Retries use exponential backoff with jitter, because Advanced Billing does not return
    /// rate-limit or <c>Retry-After</c> headers to pace against.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Whether every mandatory setting has a value.</summary>
    public bool IsConfigured => DescribeMissingSettings().Count == 0;

    /// <summary>
    /// The API base address to use, honouring <see cref="BaseUrl"/> verbatim when it is set.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var configured))
            {
                throw new UriFormatException($"'{MaxioSettings.SectionName}:{nameof(BaseUrl)}' is not an absolute URI: '{BaseUrl}'.");
            }

            // A relative request path only composes onto a base address that ends in a slash.
            return configured.AbsolutePath.EndsWith('/')
                ? configured
                : new UriBuilder(configured) { Path = configured.AbsolutePath + "/" }.Uri;
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/");
    }

    /// <summary>
    /// Names the configuration keys that still need a value, for a diagnostic the operator can act on.
    /// </summary>
    public IReadOnlyList<string> DescribeMissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add($"{SectionName}:{nameof(ApiKey)}");
        }

        // The subdomain is what the base address is derived from, so it is only required when no
        // explicit base address was given.
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add($"{SectionName}:{nameof(Subdomain)}");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add($"{SectionName}:{nameof(ProductFamilyHandle)}");
        }

        return missing;
    }
}
