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
        services.AddTransient<MaxioOnceOnlyWriteHandler>();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioOnceOnlyWriteHandler>()
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(options));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioOptions config)
    {
        var environment = ParseEnvironment(config.Environment);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = config.ApiKey ?? string.Empty,
                Password = "x"
            }
        };

        clientOptions.Server.Production.Us.Site = config.Subdomain;
        clientOptions.Server.Production.Eu.Site = config.Subdomain;

        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            if (environment == ServerEnvironment.Eu)
            {
                clientOptions.Server.Production.Eu.BaseUrl = config.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.BaseUrl = config.BaseUrl;
            }
        }

        return clientOptions;
    }

    internal static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Eu", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
