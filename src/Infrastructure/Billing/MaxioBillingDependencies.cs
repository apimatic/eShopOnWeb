using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingDependencies
{
    private const string ClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Subdomain), "Maxio:Subdomain is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required.");

        services.AddSingleton<MaxioRequestContext>();
        services.AddSingleton<SubscriptionKeyLock>();
        services.AddTransient<MaxioWriteGuardHandler>();

        services.AddHttpClient(ClientName, client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioWriteGuardHandler>();

        services.AddSingleton(serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Delay = TimeSpan.FromMilliseconds(250),
                    MaxJitter = TimeSpan.FromMilliseconds(100),
                    Timeout = TimeSpan.FromSeconds(5)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = configured.ApiKey,
                    Password = "x"
                }
            };

            options.Server.Production.Us.Site = configured.Subdomain;
            if (!string.IsNullOrWhiteSpace(configured.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = configured.BaseUrl;
            }

            var httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(ClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
