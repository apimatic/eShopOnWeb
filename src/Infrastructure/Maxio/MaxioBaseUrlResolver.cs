using System;
using Microsoft.eShopWeb.ApplicationCore;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Derives the Maxio API base address from configuration. When
/// MaxioSettings.BaseUrl is set it is used verbatim; otherwise the host is
/// derived from the configured subdomain and environment.
/// </summary>
public static class MaxioBaseUrlResolver
{
    public static string Resolve(MaxioSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.TrimEnd('/');
        }

        var subdomain = settings.Subdomain?.Trim();
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio:BaseUrl or Maxio:Subdomain must be configured. Set Maxio:Subdomain from the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        // US sites are served from *.chargify.com; EU sites from *.advancedbilling.eu.
        var host = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? "advancedbilling.eu"
            : "chargify.com";

        return $"https://{subdomain}.{host}";
    }
}
