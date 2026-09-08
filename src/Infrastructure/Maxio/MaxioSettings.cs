using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the <c>Maxio</c> configuration
/// section: <c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c>, <c>Maxio:ProductFamilyHandle</c> and the
/// optional <c>Maxio:BaseUrl</c> override. Values are supplied through environment variables /
/// user-secrets and are never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When empty, the base address is derived from the
    /// subdomain as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the absolute base address of the Maxio Advanced Billing API.
    /// </summary>
    public Uri GetApiBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl!.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide Maxio:Subdomain (and Maxio:ApiKey) via user-secrets or environment configuration.");
        }

        return new Uri($"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com/", UriKind.Absolute);
    }
}
