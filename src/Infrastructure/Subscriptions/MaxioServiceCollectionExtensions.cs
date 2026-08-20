using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(MaxioOptions.SectionName).Get<MaxioOptions>() ?? new MaxioOptions();
        services.AddSingleton(options);

        services.AddTransient<MaxioRequestHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioRequestHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            options.Validate();
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                }
            };

            clientOptions.Server.Production.Us.Site = options.Subdomain;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<SubscriptionOperationLock>();
        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }
}
