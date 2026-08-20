using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Bind from the <c>Maxio:</c> configuration section. Values come from user-secrets
/// or the <c>MAXIO_*</c> environment variables — never from committed secrets.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetApiBaseUri()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl.Trim();

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set user secret Maxio:ApiKey or environment variable MAXIO_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain or Maxio:BaseUrl must be configured. Set user secret Maxio:Subdomain or environment variable MAXIO_SITE_SUBDOMAIN.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set user secret Maxio:ProductFamilyHandle or environment variable MAXIO_DEFAULT_PRODUCT_FAMILY.");
        }
    }
}
