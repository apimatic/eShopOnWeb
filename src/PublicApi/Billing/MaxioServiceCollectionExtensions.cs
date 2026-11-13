using System;
using System.Collections.Generic;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK client runs on (tests replace its handler).</summary>
    public const string HttpClientName = "Maxio";

    // The deployment environment variables the task defines, mapped onto the Maxio:* binding keys.
    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableKeys = new Dictionary<string, string>
    {
        ["MAXIO_API_KEY"] = "Maxio:ApiKey",
        ["MAXIO_SITE_SUBDOMAIN"] = "Maxio:Subdomain",
        ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = "Maxio:ProductFamilyHandle",
        ["MAXIO_ENVIRONMENT"] = "Maxio:Environment",
    };

    /// <summary>
    /// Lets the plain <c>MAXIO_*</c> environment variables feed the <c>Maxio:*</c> keys. The source is inserted
    /// with the LOWEST precedence, so appsettings, user-secrets, <c>Maxio__*</c> variables and command-line values
    /// all win over it. Values are read from the process environment only - nothing is written anywhere.
    /// </summary>
    public static ConfigurationManager AddMaxioEnvironmentVariableFallback(this ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentVariableKeys)
        {
            var value = System.Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        if (values.Count > 0)
        {
            configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
        }
        return configuration;
    }

    /// <summary>
    /// Registers the Maxio SDK client (singleton, on a dedicated named HttpClient), the billing gateway and the
    /// subscription service. Settings are validated when the host starts.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, (sp, client) =>
            {
                // Backstop only: the SDK's per-attempt timeout (Retry.Timeout) fires first and the per-request
                // budget (a CancellationToken) bounds the whole call.
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient: recycle pooled connections so DNS changes
                // on Maxio's side are picked up without a restart.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp));
        });

        services.AddScoped<IBillingClaimStore, EfBillingClaimStore>();
        services.AddScoped<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, IServiceProvider sp)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // Maxio's Basic scheme: the API key is the username, the password is the literal "x".
            // Captured once: a rotated key takes effect on the next process start.
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            // GETs are retried on transient failures; POSTs are never re-sent (default HttpMethodsToRetry).
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds),
                MaxRetries = settings.MaxReadRetries
            },
            // The customer create carries e-mail and names: never log bodies. An explicit LoggerFactory also keeps
            // the MAXIO_ADVANCED_BILLING_LOG environment variable from switching body logging on.
            Logging = new LoggingOptions
            {
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            },
            TimeProvider = sp.GetRequiredService<TimeProvider>()
        };

        var subdomain = settings.Subdomain?.Trim();
        var baseUrl = settings.HasBaseUrl ? settings.BaseUrl!.Trim() : null;
        if (settings.IsEu)
        {
            if (!string.IsNullOrEmpty(subdomain)) options.Server.Production.Eu.Site = subdomain;
            if (baseUrl is not null) options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (!string.IsNullOrEmpty(subdomain)) options.Server.Production.Us.Site = subdomain;
            if (baseUrl is not null) options.Server.Production.Us.BaseUrl = baseUrl;
        }
        return options;
    }
}
