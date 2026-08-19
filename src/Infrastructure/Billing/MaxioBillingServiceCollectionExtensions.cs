using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<PreventRetryPostHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = AttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<PreventRetryPostHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(configuration));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClientOptions CreateClientOptions(IConfiguration configuration)
    {
        var apiKey = configuration[$"{MaxioOptions.SectionName}:ApiKey"] ?? string.Empty;
        var subdomain = configuration[$"{MaxioOptions.SectionName}:Subdomain"] ?? string.Empty;
        var baseUrl = configuration[$"{MaxioOptions.SectionName}:BaseUrl"];

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ResolveEnvironment(),
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout,
                MaxRetries = 1
            }
        };

        options.Server.Production.Us.Site = subdomain;
        options.Server.Production.Eu.Site = subdomain;

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }

        return options;
    }

    private static ServerEnvironment ResolveEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
