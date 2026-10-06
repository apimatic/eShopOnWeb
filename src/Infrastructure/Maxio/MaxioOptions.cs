using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section; values come from user-secrets or environment
/// variables and are never committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (the Basic-auth username; password is the literal "x").
    /// From MAXIO_API_KEY.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Maxio site subdomain. From MAXIO_SITE_SUBDOMAIN.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// From MAXIO_DEFAULT_PRODUCT_FAMILY.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address override. When set, it is used
    /// verbatim instead of deriving the base URL from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. MaxioBaseUrlOverride wins when present;
    /// otherwise the URL is derived from the subdomain per the OpenAPI spec's
    /// server configuration (US production by default, EU when the
    /// MAXIO_ENVIRONMENT environment variable is "EU").
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio subdomain is not configured. Set 'Maxio:Subdomain' (environment variable MAXIO_SITE_SUBDOMAIN) or 'Maxio:BaseUrl'.");
        }

        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain.Trim()}.ebilling.maxio.com"
            : $"{Subdomain.Trim()}.chargify.com";
        return $"https://{host}";
    }

    /// <summary>
    /// Validates that the minimal configuration needed to call the API is present.
    /// </summary>
    public void RequireValid()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio API key is not configured. Set 'Maxio:ApiKey' (environment variable MAXIO_API_KEY).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio site is not configured. Set 'Maxio:Subdomain' (environment variable MAXIO_SITE_SUBDOMAIN) or 'Maxio:BaseUrl'.");
        }
    }
}