namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from environment variables
/// (<c>MAXIO_API_KEY</c>, <c>MAXIO_SITE_SUBDOMAIN</c>, <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>)
/// and/or user-secrets — never from committed secret values.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set, used instead of deriving one from Subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Advanced Billing API root. A configured <see cref="BaseUrl"/> is used verbatim;
    /// otherwise the US host <c>https://{subdomain}.chargify.com</c> is derived from
    /// <see cref="Subdomain"/> (Maxio Advanced Billing authentication / site addressing).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return "https://invalid.local";
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }
}
