using System;
using System.Collections.Generic;
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

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK client is built on.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound on waiting for Maxio's response headers (the SDK retries GETs within the call budget).</summary>
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Transport backstop per attempt, slightly above the SDK's own per-attempt timeout.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableMap = new Dictionary<string, string>
    {
        ["MAXIO_API_KEY"] = "Maxio:ApiKey",
        ["MAXIO_SITE_SUBDOMAIN"] = "Maxio:Subdomain",
        ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = "Maxio:ProductFamilyHandle",
        ["MAXIO_ENVIRONMENT"] = "Maxio:Environment",
    };

    /// <summary>
    /// Maps the conventional MAXIO_* environment variables onto the <c>Maxio:</c> configuration keys
    /// (only the variables that are actually set). <c>Maxio__ApiKey</c>-style variables keep working as usual.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentVariableMap)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        return values.Count == 0 ? configuration : configuration.AddInMemoryCollection(values);
    }

    /// <summary>
    /// Registers Maxio-backed subscription billing: validated settings (the host refuses to start when they are
    /// incomplete), a long-lived SDK client on its own named <see cref="HttpClient"/>, and <see cref="ISubscriptionBillingService"/>.
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
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Settings are read once here: a rotated API key takes effect after a process restart.
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddMemoryCache();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio Basic auth: the API key is the username and the password is the literal "x".
                Username = settings.ApiKey!.Trim(),
                Password = "x"
            },
            // Writes (POST) stay out of HttpMethodsToRetry, so the SDK never resends them.
            Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
            Logging = new LoggingOptions
            {
                // Assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG variable cannot switch on body logging;
                // request bodies carry customer names and e-mail addresses.
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        // Configure the server of the environment selected above; the other environment's options are never read.
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        if (settings.IsEuRegion)
        {
            if (baseUrl is not null) options.Server.Production.Eu.BaseUrl = baseUrl;
            else options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }
        else
        {
            if (baseUrl is not null) options.Server.Production.Us.BaseUrl = baseUrl;
            else options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        }

        return options;
    }
}
