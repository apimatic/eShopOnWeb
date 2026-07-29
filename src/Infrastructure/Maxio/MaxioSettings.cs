using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the <c>Maxio:</c>
/// configuration section. Values are supplied via .NET user-secrets / environment and
/// are never committed to the repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the HTTP Basic auth username (password is a literal "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain; used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of being
    /// derived from <see cref="Subdomain"/> (useful for non-US regions or a proxy).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How payment is collected for new subscriptions. Defaults to <c>remittance</c> (invoice
    /// billing) so enrollment succeeds without capturing a card, matching the demo catalog's
    /// "payment method not required" plans. Configurable for sites that require otherwise.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Validates that the minimum required settings are present and returns the resolved API
    /// base address (no trailing slash). Throws <see cref="BillingConfigurationException"/> when
    /// misconfigured — called lazily on first use so the host still boots without billing configured.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets from the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        string baseUrl;
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            baseUrl = BaseUrl.TrimEnd('/');
        }
        else if (!string.IsNullOrWhiteSpace(Subdomain))
        {
            baseUrl = $"https://{Subdomain}.chargify.com";
        }
        else
        {
            throw new BillingConfigurationException(
                "Neither Maxio:BaseUrl nor Maxio:Subdomain is configured. Set Maxio:Subdomain via user-secrets from the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new BillingConfigurationException($"Resolved Maxio base address '{baseUrl}' is not a valid absolute URL.");
        }

        return uri;
    }
}
