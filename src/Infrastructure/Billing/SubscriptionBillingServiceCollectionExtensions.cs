using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class SubscriptionBillingServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient(MaxioClientFactory.HttpClientName, client =>
        {
            client.Timeout = MaxioClientFactory.AttemptTimeout;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioClientFactory.HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new MaxioAdvancedBillingClient(httpClient, MaxioClientFactory.CreateOptions(settings, loggerFactory));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        services.AddSingleton<IHostedService, MaxioSettingsStartupCheck>();
        return services;
    }
}
