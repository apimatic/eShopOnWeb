using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<OnceOnlyWriteHandler>();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<OnceOnlyWriteHandler>()
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var environment = MapEnvironment(configuration["MAXIO_ENVIRONMENT"]);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey,
                    Password = "x"
                }
            };

            if (environment == ServerEnvironment.Eu)
            {
                options.Server.Production.Eu.Site = maxio.Subdomain;
                if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
                }
            }
            else
            {
                options.Server.Production.Us.Site = maxio.Subdomain;
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

    private static ServerEnvironment MapEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
