using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        var options = configuration.GetSection(MaxioOptions.SectionName).Get<MaxioOptions>() ?? new MaxioOptions();
        if (!options.IsConfigured)
        {
            services.AddSingleton<ISubscriptionBillingService, UnconfiguredSubscriptionBillingService>();
            return services;
        }

        services.AddTransient<MaxioLoggingHandler>();
        services.AddTransient<OncePerWriteHandler>();
        services.AddTransient<StatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioLoggingHandler>()
            .AddHttpMessageHandler<OncePerWriteHandler>()
            .AddHttpMessageHandler<StatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return CreateClient(httpClient, maxio);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioOptions maxio)
    {
        var environment = ResolveEnvironment(maxio.Environment);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 2
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxio.ApiKey,
                Password = "x"
            }
        };

        if (environment == ServerEnvironment.Eu)
        {
            clientOptions.Server.Production.Eu.Site = maxio.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                clientOptions.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
            }
        }
        else
        {
            clientOptions.Server.Production.Us.Site = maxio.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }
        }

        return new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    internal static ServerEnvironment ResolveEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ServerEnvironment.Us;
        }

        if (ServerEnvironment.TryGetKnownValue(value, out var known) && known is not null)
        {
            return known;
        }

        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
