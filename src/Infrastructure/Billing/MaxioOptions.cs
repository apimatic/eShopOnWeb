using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from environment
/// variables / user-secrets — never from source.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from <c>Maxio:ApiKey</c> / <c>MAXIO_API_KEY</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from <c>Maxio:Subdomain</c> / <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from <c>Maxio:ProductFamilyHandle</c> / <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the Advanced Billing API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// US hosting: https://{subdomain}.chargify.com.
    /// EU hosting: https://{subdomain}.ebilling.maxio.com.
    /// Confirmed against Maxio Advanced Billing SDK environment map (9.x).
    /// </summary>
    public string ResolveBaseUrl(string? maxioEnvironment)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var host = IsEu(maxioEnvironment) ? "ebilling.maxio.com" : "chargify.com";
        return $"https://{Subdomain}.{host}";
    }

    private static bool IsEu(string? environment) =>
        string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "EBB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Europe", StringComparison.OrdinalIgnoreCase);
}
