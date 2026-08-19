using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBaseUrl
{
    public static string Resolve(MaxioOptions options, string? hostingEnvironment = null)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new BillingUnavailableException(
                "Maxio is not configured. Set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        hostingEnvironment ??= Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var isEu = string.Equals(hostingEnvironment, "EU", StringComparison.OrdinalIgnoreCase);
        var host = isEu ? "ebilling.maxio.com" : "chargify.com";
        return $"https://{options.Subdomain}.{host}";
    }
}
