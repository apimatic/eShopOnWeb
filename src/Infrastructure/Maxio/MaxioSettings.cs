using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration. Bound from the
/// <c>Maxio:</c> configuration section. Secrets (the API key) are supplied via .NET
/// user-secrets / environment configuration and never committed to the repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Used as the HTTP Basic username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim; otherwise the base URL
    /// is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions. Defaults to <c>remittance</c> (invoice
    /// billing) so shoppers can subscribe without capturing a card, honoring the "payment method
    /// not required" plan configuration. Override via <c>Maxio:PaymentCollectionMethod</c> (e.g.
    /// <c>invoice</c> for legacy Statements Architecture sites, or <c>automatic</c> to require a card).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Resolves the effective API base address per the spec's server templating.</summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request paths resolve correctly.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Throws when the configuration is insufficient to talk to Maxio.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets (from the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration requires either Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) or an explicit Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets (from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }
    }
}
