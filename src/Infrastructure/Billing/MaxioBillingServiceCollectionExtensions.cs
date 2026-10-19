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

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound on waiting for Maxio's response headers (the SDK retries GETs within the request budget).</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionBillingService"/>.
    /// Settings bind from the <c>Maxio</c> section and are validated when the host starts.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services)
    {
        services.AddOptions<MaxioSettings>()
            .BindConfiguration(MaxioSettings.SectionName)
            .PostConfigure<IConfiguration>(ApplyEnvironmentVariableFallbacks)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // A named client keeps this pipeline (timeout, connection lifetime) off the app's shared default HttpClient.
        services.AddHttpClient(HttpClientName, c =>
            {
                // Backstop only: unlike the SDK's own per-attempt timeout, this one is not retried.
                c.Timeout = AttemptTimeout + TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Options are read once here: a rotated API key takes effect on the next process restart.
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    // Maxio's Basic scheme: the API key is the username and the password is the literal "x".
                    Password = "x"
                },
                // POST stays out of HttpMethodsToRetry: creates are never resent by the SDK.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = AttemptTimeout
                },
                Logging = new LoggingOptions
                {
                    // Set explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG environment variable can never
                    // switch on body logging: customer create bodies carry personal data.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                },
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                if (settings.IsEuRegion)
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl.Trim();
                else
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
            }
            else if (settings.IsEuRegion)
            {
                options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>
    /// Deployments that only provide the raw MAXIO_* environment variables still bind: each fills its
    /// <c>Maxio:</c> key when that key is not configured. An explicit <c>Maxio:</c> value always wins.
    /// </summary>
    private static void ApplyEnvironmentVariableFallbacks(MaxioSettings settings, IConfiguration configuration)
    {
        settings.ApiKey = FirstNonBlank(settings.ApiKey, configuration["MAXIO_API_KEY"]);
        settings.Subdomain = FirstNonBlank(settings.Subdomain, configuration["MAXIO_SITE_SUBDOMAIN"]);
        settings.ProductFamilyHandle = FirstNonBlank(settings.ProductFamilyHandle, configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"]);
        settings.Environment = FirstNonBlank(settings.Environment, configuration["MAXIO_ENVIRONMENT"]);
    }

    private static string? FirstNonBlank(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
