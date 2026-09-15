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

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddTransient<MaxioOnceWriteHandler>();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioOnceWriteHandler>()
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(maxio));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioOptions maxio)
    {
        var environment = ParseEnvironment(maxio.Environment);
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
                Username = maxio.ApiKey ?? string.Empty,
                Password = "x"
            }
        };

        if (environment == ServerEnvironment.Eu)
        {
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
            }
            else
            {
                options.Server.Production.Eu.Site = maxio.Subdomain;
            }
        }
        else if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = maxio.Subdomain;
        }

        return options;
    }

    internal static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
