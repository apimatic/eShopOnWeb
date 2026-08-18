namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Secret values must come from
/// environment variables or user-secrets — never from source-controlled files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from <c>MAXIO_API_KEY</c> / <c>Maxio:ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from <c>MAXIO_SITE_SUBDOMAIN</c> / <c>Maxio:Subdomain</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c> / <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> via the OpenAPI server template
    /// <c>https://{site}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Advanced Billing API root from the spec's server template
    /// <c>https://{site}.chargify.com</c>, unless <see cref="BaseUrl"/> overrides it.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return "https://localhost";
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }
}
