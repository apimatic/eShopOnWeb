using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "Maxio:ApiKey is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Subdomain), "Maxio:Subdomain is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.");

        services.AddSingleton<MaxioRequestContext>();
        services.AddSingleton<SubscriptionOperationLock>();
        services.AddTransient<MaxioHttpHandler>();
        services.AddHttpClient(MaxioOptions.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(18);
            })
            .AddHttpMessageHandler<MaxioHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var logger = serviceProvider.GetRequiredService<ILogger<MaxioAdvancedBillingClient>>();
            var sdkOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15),
                    OnRetry = attempt => logger.LogWarning(
                        "Retrying Maxio request (attempt {AttemptNumber}) after {Delay}",
                        attempt.AttemptNumber,
                        attempt.Delay)
                }
            };

            sdkOptions.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                sdkOptions.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(MaxioOptions.HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, sdkOptions);
        });

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
