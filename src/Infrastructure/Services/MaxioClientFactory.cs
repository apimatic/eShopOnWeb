using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Builds a <see cref="MaxioAdvancedBillingClient"/> from <see cref="MaxioSettings"/>. This is
/// the single place that resolves the outbound base URL (§2.3): an explicit <c>Maxio:BaseUrl</c>
/// wins verbatim; otherwise the host is derived from <c>Maxio:Subdomain</c>. Shared by the DI
/// registration and by tests that need the same client construction without a DI container.
/// </summary>
public static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Environment = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
            Retry = RetryOptions.Default()
        };

        ApplyServerOverride(options.Server, settings);

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    private static void ApplyServerOverride(ServerOptions server, MaxioSettings settings)
    {
        var hasExplicitOverride = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        var isEu = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);

        if (isEu)
        {
            if (hasExplicitOverride)
            {
                server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                server.Production.Eu.Site = settings.Subdomain;
            }
        }
        else
        {
            if (hasExplicitOverride)
            {
                server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                server.Production.Us.Site = settings.Subdomain;
            }
        }
    }
}
