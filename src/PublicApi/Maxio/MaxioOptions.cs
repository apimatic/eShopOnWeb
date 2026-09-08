using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the <c>Maxio</c> configuration section. Values come from the
/// <c>MAXIO_API_KEY</c>, <c>MAXIO_SITE_SUBDOMAIN</c> and <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>
/// environment variables (see <c>Program.cs</c>); nothing is hard-coded here.
/// </summary>
public class MaxioOptions
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>Maxio API key (from <c>MAXIO_API_KEY</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from <c>MAXIO_SITE_SUBDOMAIN</c>), e.g. <c>cp-exp-7</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family holding the subscription plans (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base address override (e.g. <c>https://{subdomain}.chargify.com</c>).
    /// When set, it is used verbatim as the API base address; otherwise the address is derived
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets the API base address: <see cref="BaseUrl"/> when configured, otherwise
    /// <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public Uri BaseAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return new Uri(BaseUrl.Trim().TrimEnd('/'), UriKind.Absolute);
            }

            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                throw new MaxioConfigurationException(
                    "Maxio is not configured. Set Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) or provide Maxio:BaseUrl.");
            }

            return new Uri($"https://{Subdomain.Trim().TrimEnd('.')}.chargify.com", UriKind.Absolute);
        }
    }
}
