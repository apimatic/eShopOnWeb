using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Billing API (Maxio Advanced Billing) connection settings.
/// Bound from the <c>Maxio</c> configuration section. Secret values must come from
/// user-secrets or environment variables, never from source files.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute API base address. When set, used verbatim instead of
    /// deriving <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    public string ResolveApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }
}
