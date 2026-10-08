using System;
using System.Net.Http;
using AdyenApIs;
using AdyenApIs.Core.Configuration;
using AdyenApIs.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class AdyenServiceCollectionExtensions
{
    /// <summary>Named HttpClient carrying only Adyen traffic, so its timeout and handler never leak to other consumers.</summary>
    public const string HttpClientName = "Adyen";

    public static IServiceCollection AddAdyenPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdyenSettings>()
            .Bind(configuration.GetSection(AdyenSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AdyenSettings>, AdyenSettingsValidator>());

        services.AddHttpClient(HttpClientName, client => client.Timeout = AdyenPaymentGateway.HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are captured once here: a rotated API key takes effect on the next process start.
        services.TryAddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AdyenSettings>>().Value;
            var options = new AdyenApIsClientOptions
            {
                ApiKeyAuth = settings.ApiKey!.Trim(),
                // The SDK's only environment; its Checkout base URL is Adyen's test endpoint.
                Environment = ServerEnvironment.Production,
                // Every call here is a POST and must never be resent blindly; the gateway does its own same-key resend.
                Retry = RetryOptions.Disabled() with { Timeout = AdyenPaymentGateway.PerAttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Set explicitly so the ADYENAPISCLIENT_LOG environment variable can never switch body logging on.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    // Request bodies carry (encrypted) card data and the holder's name.
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System,
            };
            if (!string.IsNullOrWhiteSpace(settings.CheckoutBaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.CheckoutBaseUrl.Trim().TrimEnd('/');
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new AdyenApIsClient(httpClient, options);
        });

        services.TryAddScoped<IPaymentGateway, AdyenPaymentGateway>();
        services.TryAddScoped<IPaymentStateStore, PaymentStateStore>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
