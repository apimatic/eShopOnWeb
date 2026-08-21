using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                client.BaseAddress = new Uri(options.ResolveBaseUrl());
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<MaxioAuthenticationHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
