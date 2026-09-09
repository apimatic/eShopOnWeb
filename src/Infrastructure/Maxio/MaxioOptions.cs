using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the "Maxio"
/// configuration section. Credential values must come from user-secrets / environment
/// variables and must never be committed to the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (basic-auth username). Source: MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Advanced Billing site subdomain. Source: MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscribable plans. Source: MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override (e.g. "https://my-site.chargify.com").
    /// When set it is used as-is instead of deriving the URL from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Advanced Billing environment per the API spec: "US" (default) or "EU".</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Resolves the API base address. The explicit <see cref="BaseUrl"/> override wins;
    /// otherwise the URL is templated from the site subdomain as defined by the Maxio
    /// OpenAPI spec server configuration (US: {site}.chargify.com, EU: {site}.ebilling.maxio.com).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var subdomain = Subdomain.Trim();
        if (subdomain.Length == 0)
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return Environment.Trim().ToUpperInvariant() switch
        {
            "US" or "" => $"https://{subdomain}.chargify.com",
            "EU" => $"https://{subdomain}.ebilling.maxio.com",
            _ => throw new InvalidOperationException(
                $"Unknown Maxio environment '{Environment}'. Supported per the API spec: US, EU.")
        };
    }
}
