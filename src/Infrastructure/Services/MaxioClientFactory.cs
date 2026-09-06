using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient CreateClient(IConfiguration configuration, HttpClient httpClient, ILogger logger)
    {
        var apiKey = configuration["Maxio:ApiKey"];
        var subdomain = configuration["Maxio:Subdomain"];
        var baseUrl = configuration["Maxio:BaseUrl"];

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey configuration is required");
        }

        if (string.IsNullOrEmpty(subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain configuration is required");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3
            }
        };

        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = subdomain;
        }

        logger.LogInformation($"Initializing Maxio client with subdomain: {subdomain}");

        return new MaxioAdvancedBillingClient(httpClient: httpClient, options: options);
    }
}
