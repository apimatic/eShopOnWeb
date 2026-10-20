using System;
using System.Collections.Generic;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioConfigurationExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> the Maxio SDK client is built over (kept off the shared default client).</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt backstop on waiting for response headers; ends the call the first time it fires.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The SDK's own per-attempt timeout (retried on GETs only).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The environment variable names this deployment supplies, mapped onto the <c>Maxio:</c> keys.
    /// Standard <c>Maxio__ApiKey</c>-style variables work without this.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EnvironmentFallbacks = new Dictionary<string, string>
    {
        ["Maxio:ApiKey"] = "MAXIO_API_KEY",
        ["Maxio:Subdomain"] = "MAXIO_SITE_SUBDOMAIN",
        ["Maxio:ProductFamilyHandle"] = "MAXIO_DEFAULT_PRODUCT_FAMILY",
    };

    /// <summary>
    /// For each <c>Maxio:</c> key that configuration left blank, takes the value from its
    /// <c>MAXIO_*</c> environment variable. In memory only: nothing is persisted or logged.
    /// Call it after every other configuration source has been added.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentFallback(
        this IConfigurationBuilder builder,
        IConfiguration current,
        Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var values = new Dictionary<string, string?>();
        foreach (var (key, variable) in EnvironmentFallbacks)
        {
            if (!string.IsNullOrWhiteSpace(current[key])) continue;
            var value = readEnvironment(variable);
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
        }

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder;
    }

    /// <summary>
    /// Registers the validated Maxio settings (the host refuses to start when one is missing), the Maxio
    /// Advanced Billing SDK client (a singleton over a named HttpClient) and <see cref="SubscriptionBillingService"/>.
    /// The client options are built once, when the client is first resolved, so a rotated API key takes
    /// effect after a process restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding this HttpClient: recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.TryAddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>());
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        // Process-lifetime cache of the site's invoicing architecture (only a successful, determinate read is kept).
        services.TryAddSingleton<SiteCollectionMethodCache>();
        services.AddScoped<SubscriptionBillingService>();
        return services;
    }

    /// <summary>Builds the SDK client options from validated settings.</summary>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(
        MaxioSettings settings,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
            TimeProvider = timeProvider,
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                // Maxio Basic auth: the user name is the API key; the password is the fixed value "x".
                Username = settings.ApiKey!.Trim(),
                Password = "x"
            },
            // Default HttpMethodsToRetry (GET/HEAD/PUT/OPTIONS) is kept: the two POST writes are never resent by the SDK.
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = AttemptTimeout
            },
            Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
            {
                // Assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG variable can never switch body logging on.
                LoggerFactory = loggerFactory,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                // CreateCustomer carries personal data (name, email): never log request bodies.
                LogRequestBody = false
            }
        };

        if (settings.HasBaseUrlOverride)
        {
            var baseUrl = settings.BaseUrl!.Trim();
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            var site = settings.Subdomain!.Trim();
            options.Server.Production.Us.Site = site;
            options.Server.Production.Eu.Site = site;
        }

        return options;
    }
}
