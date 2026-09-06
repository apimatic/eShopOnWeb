using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers Maxio-backed subscription billing.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Name of the dedicated <see cref="HttpClient"/> this integration owns. Deliberately not the
    /// default unnamed client: the timeout, the primary handler and the write-once handler
    /// configured here must not change behaviour for every other consumer in the app.
    /// </summary>
    public const string HttpClientName = "maxio-advanced-billing";

    /// <summary>
    /// Binds the <c>Maxio:</c> configuration section and registers
    /// <see cref="ISubscriptionService"/>. When the section is missing or incomplete an
    /// <see cref="UnconfiguredSubscriptionService"/> is registered instead, so the host still
    /// starts and only the subscription endpoints report the problem.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = MaxioSettings.Load(configuration);
        var problems = settings.Validate();

        services.AddSingleton(settings);

        if (problems.Count > 0)
        {
            services.AddSingleton<ISubscriptionService>(provider =>
                new UnconfiguredSubscriptionService(
                    problems,
                    provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UnconfiguredSubscriptionService>>()));

            return services;
        }

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioCallContextHandler>();
        services.AddTransient<MaxioHttpLoggingHandler>();

        var httpClientBuilder = services.AddHttpClient(HttpClientName, client =>
        {
            // Bounds a single attempt. Left at the 100s default a hung provider would pin a
            // request thread for well over a minute; the total call budget lives on the
            // CancellationToken in MaxioSubscriptionService.
            client.Timeout = settings.AttemptTimeout;
        });

        if (settings.LogHttpTraffic)
        {
            httpClientBuilder.AddHttpMessageHandler<MaxioHttpLoggingHandler>();
        }

        httpClientBuilder
            .AddHttpMessageHandler<MaxioCallContextHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient for the process
                // lifetime, so the factory's handler rotation never reaches it. Without this a DNS
                // change would be cached indefinitely.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings));
        });

        services.AddSingleton<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            // A Maxio sandbox is an ordinary site subdomain on the US host; the SDK has no
            // separate sandbox environment.
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,

                // Maxio's documented basic-auth convention for API-key authentication: the key is
                // the user name and the password is an ignored placeholder.
                Password = "x"
            },
            Retry = RetryOptions.Default() with { Timeout = settings.AttemptTimeout }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim. The default base URL is a template containing a {site} placeholder;
            // an override without that placeholder passes through unchanged, so the subdomain is
            // not substituted into anything.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
        }

        return options;
    }
}
