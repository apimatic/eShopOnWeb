using System;
using System.Net.Http;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Registers the Maxio Advanced Billing client and the subscription billing boundary.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// A dedicated, named HttpClient rather than the default one the SDK's own DI extension would
    /// resolve: the timeout, primary handler and message handlers below then apply to billing calls
    /// only, instead of to every other unnamed HttpClient consumer in the application.
    /// </summary>
    public const string MaxioHttpClientName = "maxio-advanced-billing";

    /// <summary>The password half of the provider's basic-auth scheme; the API key is the username.</summary>
    private const string BasicAuthPassword = "x";

    /// <summary>Backstop for a hung provider. The SDK does not retry a timeout, so this ends the call.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Bounds a single attempt. The default is 100s, which is an outage, not a timeout.</summary>
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Needed because the SDK client below is a singleton, so factory handler rotation never reaches it.</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        Guard.Against.Null(services, nameof(services));
        Guard.Against.Null(configuration, nameof(configuration));

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        services.AddTransient<MaxioRequestLoggingHandler>();
        services.AddTransient<MaxioWriteOnceHandler>();

        services.AddHttpClient(MaxioHttpClientName, client => client.Timeout = HttpClientTimeout)
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>()
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = PooledConnectionLifetime
            });

        services.AddSingleton<SubscriberLockRegistry>();
        services.AddSingleton(CreateClient);
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static MaxioAdvancedBillingClient CreateClient(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(BillingServiceCollectionExtensions).FullName!);

        var missing = settings.MissingSettings();
        if (missing.Count > 0)
        {
            // Names only, never values. The subscription endpoints answer 503 until these are supplied.
            logger.LogWarning(
                "Maxio billing is not fully configured; the subscription endpoints will report 503 until these configuration keys are supplied: {MissingKeys}.",
                string.Join(", ", missing));
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout }
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!.Trim(),
                Password = BasicAuthPassword
            };
        }

        // Mode (a): the base URL template substitutes the site, so the address is derived from the subdomain.
        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        }

        // Mode (b): an explicit override wins and is used verbatim - a URL with no placeholder passes straight through.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!.Trim();
        }

        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
