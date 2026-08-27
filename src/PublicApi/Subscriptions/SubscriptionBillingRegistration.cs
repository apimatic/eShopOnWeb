using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionBillingRegistration
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddSubscriptionBilling(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton<MaxioHttpCallContext>();
        services.AddTransient<MaxioHttpPipelineHandler>();
        services.AddSingleton<SubscriptionOperationLock>();

        services.AddHttpClient(HttpClientName, httpClient =>
            {
                httpClient.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioHttpPipelineHandler>();

        services.AddSingleton(serviceProvider =>
        {
            var maxio = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            maxio.EnsureValid();

            var sdkOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(8)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey,
                    Password = "x"
                }
            };

            if (string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                sdkOptions.Server.Production.Us.Site = maxio.Subdomain;
            }
            else
            {
                sdkOptions.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, sdkOptions);
        });

        services.AddSingleton<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
