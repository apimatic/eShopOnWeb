using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK client uses (kept off the shared default client).</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt timeout the SDK applies (its own expiry is retried on GET).</summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Transport backstop per attempt; not retried. Must exceed <see cref="PerAttemptTimeout"/>.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers subscription billing: <c>Maxio:</c> settings with startup fail-fast, the SDK client as a
    /// long-lived singleton over a named HttpClient, the scoped gateway, the claim store and the service.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient: recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            // Options are built once, here, and captured by the singleton: a rotated API key takes effect on restart.
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(), sp.GetService<TimeProvider>());
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton(new MaxioGatewayLimits());
        services.AddScoped<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<IBillingClaimStore, BillingClaimStore>();
        services.AddScoped<IShopperProfileProvider, IdentityShopperProfileProvider>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Builds the SDK client options from validated settings. Shared by DI and by tests so both run the same
    /// base-URL, auth, retry and logging configuration.
    /// </summary>
    public static MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions CreateClientOptions(
        MaxioSettings settings, ILoggerFactory loggerFactory, TimeProvider? timeProvider = null)
    {
        var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            // US region (SDK default). A sandbox is a separate site selected by Maxio:Subdomain / Maxio:BaseUrl.
            Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
            // Default verbs: POST is never resent by the SDK. Do not widen HttpMethodsToRetry.
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { Timeout = PerAttemptTimeout },
            Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
            {
                // Assigned explicitly: disables the MAXIOADVANCEDBILLINGCLIENT_LOG environment switch.
                LoggerFactory = loggerFactory,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                // The CreateCustomer body carries name and e-mail: never log request bodies.
                LogRequestBody = false,
            },
            TimeProvider = timeProvider ?? TimeProvider.System,
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x",
            },
        };

        if (settings.HasBaseUrlOverride)
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
        }

        return options;
    }
}
