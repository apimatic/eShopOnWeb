using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from environment variables
/// (<c>MAXIO_API_KEY</c>, <c>MAXIO_SITE_SUBDOMAIN</c>, <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>)
/// or .NET user-secrets — never from committed files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as Basic-auth username (password is <c>x</c> per the spec).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Advanced Billing site subdomain used to derive <c>https://{subdomain}.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address instead of deriving
    /// one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.TrimEnd('/') + "/";
            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio:BaseUrl or Maxio:Subdomain must be configured to call Advanced Billing.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    public void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }
}
