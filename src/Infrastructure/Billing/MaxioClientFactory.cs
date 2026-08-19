using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioOptions settings)
    {
        var environment = ResolveEnvironment(settings.Environment);
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            };
        }

        ApplyServer(options, environment, settings);
        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    internal static ServerEnvironment ResolveEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Eu", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }

    private static void ApplyServer(
        MaxioAdvancedBillingClientOptions options,
        ServerEnvironment environment,
        MaxioOptions settings)
    {
        if (environment == ServerEnvironment.Eu)
        {
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }

            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }
    }
}
