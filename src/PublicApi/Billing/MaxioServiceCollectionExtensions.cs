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

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<MaxioRequestLoggingHandler>();
        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>()
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var environment = ResolveEnvironment();

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey,
                    Password = "x"
                }
            };

            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                if (environment == ServerEnvironment.Eu)
                {
                    options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
                }
                else
                {
                    options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
                }
            }
            else if (!string.IsNullOrWhiteSpace(maxio.Subdomain))
            {
                if (environment == ServerEnvironment.Eu)
                {
                    options.Server.Production.Eu.Site = maxio.Subdomain;
                }
                else
                {
                    options.Server.Production.Us.Site = maxio.Subdomain;
                }
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    private static ServerEnvironment ResolveEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
