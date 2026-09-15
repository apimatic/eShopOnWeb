using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Resolves the Advanced Billing API origin from settings. Official Maxio SDK hosts:
/// US <c>https://{site}.chargify.com</c>, EU <c>https://{site}.ebilling.maxio.com</c>.
/// <see cref="MaxioSettings.BaseUrl"/> wins when set.
/// </summary>
public static class MaxioBaseUrlResolver
{
    public const string UsHostFormat = "https://{0}.chargify.com";
    public const string EuHostFormat = "https://{0}.ebilling.maxio.com";

    public static string Resolve(MaxioSettings settings, string? environment = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var isEu = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase);
        return string.Format(isEu ? EuHostFormat : UsHostFormat, settings.Subdomain.Trim());
    }
}
