using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Sensitive values (API key) are provided via
/// user-secrets or environment variables and must never be committed.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (secret; supplied via user-secrets / MAXIO_API_KEY).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain, e.g. "acme" for https://acme.chargify.com (supplied via MAXIO_SITE_SUBDOMAIN).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans
    /// (supplied via MAXIO_DEFAULT_PRODUCT_FAMILY).
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base URL override (e.g. an EU site at
    /// https://site.ebilling.maxio.com). When set, it is used verbatim instead of
    /// the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used when a subscribe request does not name a plan.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    public Uri EffectiveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}
