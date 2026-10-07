using System;
using System.Net.Http;
using AdyenApIs;
using AdyenApIs.Core.Configuration;
using AdyenApIs.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

public static class AdyenServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> dedicated to Adyen (tests replace its primary handler).</summary>
    public const string HttpClientName = "Adyen";

    /// <summary>Bound on one HTTP attempt to Adyen. Two attempts (send + settle) stay inside the request budget.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Transport-level backstop, slightly above <see cref="AttemptTimeout"/>.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Registers card payments through Adyen. The host refuses to start when an <c>Adyen:</c> setting is
    /// missing or invalid. Settings are read once, so a rotated API key takes effect on restart.
    /// </summary>
    public static IServiceCollection AddAdyenPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdyenSettings>()
            .Bind(configuration.GetSection(AdyenSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdyenSettings>, AdyenSettingsValidator>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keeps DNS fresh behind the long-lived singleton client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp => CreateClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IOptions<AdyenSettings>>().Value,
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            AttemptTimeout));

        services.AddSingleton<IPaymentGateway, AdyenPaymentGateway>();
        services.AddScoped<IPaymentOperationLock, EfPaymentOperationLockStore>();

        return services;
    }

    /// <summary>Builds the Adyen client exactly as the application uses it.</summary>
    public static AdyenApIsClient CreateClient(HttpClient httpClient, AdyenSettings settings, ILoggerFactory loggerFactory,
        TimeProvider timeProvider, TimeSpan attemptTimeout)
    {
        var options = new AdyenApIsClientOptions
        {
            // The only environment the SDK declares; its hosts are Adyen's test hosts.
            Environment = ServerEnvironment.Production,
            ApiKeyAuth = settings.ApiKey,
            // Every call here is a POST that moves money: the SDK must never resend it on its own.
            // The gateway's explicit same-idempotency-key resend is the only second send.
            Retry = RetryOptions.Disabled() with { Timeout = attemptTimeout },
            // Request bodies carry (encrypted) card data: never log bodies or headers. Assigning the
            // factory explicitly also disarms the SDK's logging environment variable.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
            TimeProvider = timeProvider,
        };
        return new AdyenApIsClient(httpClient, options);
    }
}
