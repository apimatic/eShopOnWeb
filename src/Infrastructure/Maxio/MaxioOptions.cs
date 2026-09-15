using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from environment / user-secrets,
/// never from committed settings.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps to <c>Maxio:ApiKey</c> (from <c>MAXIO_API_KEY</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps to <c>Maxio:Subdomain</c> (from <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps to <c>Maxio:ProductFamilyHandle</c> (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. Maps to <c>Maxio:BaseUrl</c>. When set, used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Advanced Billing API root per the OpenAPI servers entry
    /// <c>https://{site}.chargify.com</c>, unless <see cref="BaseUrl"/> overrides it.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));
}
