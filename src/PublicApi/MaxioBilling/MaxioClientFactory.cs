using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient Create(MaxioSettings settings, IServiceProvider serviceProvider)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(MaxioBillingService.HttpClientName);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 2,
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x",
            },
        };
        options.Server.Production.Us.Site = settings.Subdomain!;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
