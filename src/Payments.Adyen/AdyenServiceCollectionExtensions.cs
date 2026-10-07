using System;
using System.Net.Http;
using AdyenApIs;
using AdyenApIs.Core.Configuration;
using AdyenApIs.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Payments.Adyen;

public static class AdyenServiceCollectionExtensions
{
    /// <summary>Named HttpClient used only by the Adyen SDK client (its timeout and handler stay off the default client).</summary>
    public const string HttpClientName = "Adyen";

    /// <summary>
    /// Bound on one HTTP attempt to Adyen (waiting for response headers). The SDK never resends a POST and its retries
    /// are disabled here, so one attempt is one call; the request-level budget bounds the total.
    /// </summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddAdyenPayments(this IServiceCollection services, IConfiguration configuration,
        string defaultReturnUrl)
    {
        services.AddOptions<AdyenSettings>()
            .Bind(configuration.GetSection(AdyenSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (string.IsNullOrWhiteSpace(settings.ReturnUrl))
                    settings.ReturnUrl = defaultReturnUrl;
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdyenSettings>, AdyenSettingsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, client => client.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient; recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Throws OptionsValidationException (naming the missing key) if configuration is incomplete.
            var settings = sp.GetRequiredService<IOptions<AdyenSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new AdyenApIsClient(httpClient,
                BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
        });

        services.AddSingleton<IPaymentGateway, AdyenPaymentGateway>();
        return services;
    }

    internal static AdyenApIsClientOptions BuildClientOptions(AdyenSettings settings, ILoggerFactory loggerFactory, TimeProvider time) =>
        new()
        {
            ApiKeyAuth = settings.ApiKey,
            // The SDK's only environment; its Checkout base URL is Adyen's test endpoint (checkout-test).
            Environment = ServerEnvironment.Production,
            Retry = RetryOptions.Disabled() with { Timeout = AttemptTimeout },
            TimeProvider = time,
            // Assigned explicitly so the SDK's log environment variable can never switch body logging on:
            // payment requests carry (encrypted) card data and the holder's name.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
        };
}
