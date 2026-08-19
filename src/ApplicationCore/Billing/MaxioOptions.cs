using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Secret values must come from
/// environment variables or user-secrets — never from committed files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from <c>MAXIO_API_KEY</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the Advanced Billing API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Advanced Billing API origin from the OpenAPI server template
    /// <c>https://{site}.chargify.com</c>, the EU template
    /// <c>https://{site}.ebilling.maxio.com</c>, or a configured <see cref="BaseUrl"/> override.
    /// </summary>
    public string ResolveApiBaseUrl(string? hostingRegion = null)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var isEu = string.Equals(hostingRegion, "EU", StringComparison.OrdinalIgnoreCase);
        return isEu
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";
    }
}
