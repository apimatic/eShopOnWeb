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

public static class MaxioBillingRegistration
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Subdomain), "Maxio:Subdomain is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(options => string.IsNullOrWhiteSpace(options.BaseUrl) || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Maxio:BaseUrl must be an absolute URL.");

        services.AddSingleton<MaxioWriteScopeAccessor>();
        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddSingleton<AsyncKeyedLock>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(provider =>
        {
            var configured = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = configured.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (string.IsNullOrWhiteSpace(configured.BaseUrl))
            {
                options.Server.Production.Us.Site = configured.Subdomain;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = configured.BaseUrl;
            }

            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
