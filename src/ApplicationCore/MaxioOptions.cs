using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb;

/// <summary>
/// Billing API (Maxio Advanced Billing) settings. Bind from the <c>Maxio</c> configuration
/// section. Secret values must come from environment variables or user-secrets — never from
/// source files in this repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }
}
