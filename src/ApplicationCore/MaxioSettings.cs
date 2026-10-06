using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values must come from configuration (environment/user-secrets), never from source control.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    /// <summary>Maxio (Advanced Billing / Chargify) API key used as the Basic-auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Advanced Billing site subdomain, e.g. "my-site".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> (e.g. EU-hosted sites).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(ApiKey)
            && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain))
            && !string.IsNullOrWhiteSpace(ProductFamilyHandle);
    }

    /// <summary>
    /// Resolves the Advanced Billing API base address. US sites are served from
    /// https://{subdomain}.chargify.com; other deployments (EU hosting, proxies, mocks)
    /// can be addressed through the optional Maxio:BaseUrl override.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio integration is not configured. Set Maxio:Subdomain (or Maxio:BaseUrl) and Maxio:ApiKey.");
        }

        return $"https://{Subdomain.Trim().TrimEnd('.')}.chargify.com";
    }
}
