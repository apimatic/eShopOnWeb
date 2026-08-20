using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(MaxioBillingClientFactory.HttpClientName, client =>
            {
                client.Timeout = MaxioBillingClientFactory.AttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = MaxioBillingClientFactory.PooledConnectionLifetime
            })
            .AddHttpMessageHandler(() => new MaxioSingleSendHandler())
            .AddHttpMessageHandler(() => new MaxioStatusCaptureHandler());

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(MaxioBillingClientFactory.HttpClientName);
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return MaxioBillingClientFactory.Create(httpClient, settings);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
