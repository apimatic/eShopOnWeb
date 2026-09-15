using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    public const string HttpClientName = "Maxio";

    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioOptions settings, string? environmentName)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ResolveEnvironment(environmentName),
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 2
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    private static ServerEnvironment ResolveEnvironment(string? environmentName)
    {
        if (string.Equals(environmentName, "EU", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Eu", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
