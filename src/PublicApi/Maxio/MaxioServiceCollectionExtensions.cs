using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MaxioOptions.CONFIG_NAME).Get<MaxioOptions>() ?? new MaxioOptions();
        services.AddSingleton(options);

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = HttpClientTimeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = options.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
