using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the dedicated HttpClient the Maxio client uses (kept off the shared default client).</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound inside the SDK's retry pipeline.</summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>HttpClient backstop; fires once per attempt and is not retried.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers the Maxio-backed <see cref="ISubscriptionBillingService"/>, the Maxio client and the
    /// startup validation of the <c>Maxio:</c> settings. Call it only from hosts that serve the
    /// subscription endpoints: a host that registers it refuses to start without the settings.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding this HttpClient for the process lifetime;
                // recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options (and the API key) are read once, when the singleton is first resolved; a rotated
        // key takes effect after a process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(), sp.GetService<TimeProvider>());
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>Builds the Maxio client options from validated settings.</summary>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(
        MaxioSettings settings,
        ILoggerFactory loggerFactory,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)} is not configured.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            // Maxio's sandbox is a separate site, not a separate environment: the site is chosen by
            // Maxio:Subdomain (or Maxio:BaseUrl), and the environment stays the SDK default.
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey.Trim(),
                // Maxio's Basic scheme: the user name is the API key and the password is the constant "x".
                Password = "x"
            },
            // Default retried verbs exclude POST, so CreateCustomer/CreateSubscription are never resent.
            Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout, MaxRetries = 2 },
            // Set explicitly so the SDK's logging environment variable can never switch on body logging;
            // request bodies carry personal data (customer e-mail and name).
            Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false },
            TimeProvider = timeProvider ?? TimeProvider.System
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain.Trim();
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used as the base address as given; only a trailing slash is dropped so paths do not double it.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        }

        return options;
    }
}
