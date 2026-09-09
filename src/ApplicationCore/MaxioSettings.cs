using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio"
/// configuration section; secrets are supplied via user-secrets / environment
/// variables and must never be committed.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>API key of the Advanced Billing site (Basic auth username; password is fixed "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, e.g. "acme" in https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// instead of deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family containing the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Resolves the API base address, honoring the BaseUrl override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) must be configured.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
