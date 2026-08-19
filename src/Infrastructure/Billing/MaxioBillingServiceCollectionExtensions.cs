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

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<MaxioStatusCaptureHandler>();
        services.AddTransient<MaxioWriteOnceHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = AttemptTimeout;
            })
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var environmentName = sp.GetRequiredService<IConfiguration>()["MAXIO_ENVIRONMENT"];
            var environment = ResolveEnvironment(environmentName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                Retry = RetryOptions.Default() with
                {
                    Timeout = AttemptTimeout,
                    MaxRetries = 1
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey ?? string.Empty,
                    Password = "x"
                }
            };

            if (environment == ServerEnvironment.Eu)
            {
                options.Server.Production.Eu.Site = maxio.Subdomain ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
                }
            }
            else
            {
                options.Server.Production.Us.Site = maxio.Subdomain ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
                }
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    private static ServerEnvironment ResolveEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Eu", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
