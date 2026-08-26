using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Values are supplied via user-secrets or environment
/// variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY);
/// never hard-code them.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (sent as the Basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The subdomain of the Maxio site (the {site} server variable in the OpenAPI spec).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Collection method used at signup (spec: Collection-Method — automatic,
    /// remittance, prepaid, invoice). Defaults to "remittance" so subscribing
    /// works without card capture: an invoice is issued instead of charging a
    /// payment method on file. Set to "automatic" on sites where a payment
    /// profile is collected.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Resolves the API base address per the spec's server templating
    /// ("https://{site}.chargify.com"), unless an explicit BaseUrl override is set.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set Maxio:Subdomain (env MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set Maxio:ApiKey (env MAXIO_API_KEY) via user-secrets or environment.");
        }

        // Base address resolution throws when neither BaseUrl nor Subdomain is set.
        _ = ResolveBaseAddress();
    }
}
