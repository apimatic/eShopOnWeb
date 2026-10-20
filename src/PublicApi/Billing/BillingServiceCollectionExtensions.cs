using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> the Maxio client sends through; kept off the shared default client.</summary>
    public const string MaxioHttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. The host refuses to start when a
    /// required <c>Maxio:</c> setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(MaxioHttpClientName, client =>
            {
                // Backstop for one attempt; the SDK's own per-attempt timeout (below) fires first.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton, so recycle pooled connections to pick up DNS changes.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Built once: a rotated API key takes effect after a process restart.
        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings, serviceProvider.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IBillingClaimStore, EfBillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>The client options every Maxio call runs with: auth, server, retry and logging policy.</summary>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            // Captured once at construction; the server overrides below are set on this same environment.
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x"
            },
            // GETs are retried; POSTs never are (HttpMethodsToRetry stays at its default).
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(10)
            },
            // Customer creation carries personal data: never log bodies, and assign the logger factory
            // explicitly so the SDK's logging environment variable cannot switch body logging on.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        }

        return options;
    }
}
