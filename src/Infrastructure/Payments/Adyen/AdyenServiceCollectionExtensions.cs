using System;
using System.Collections.Generic;
using System.Net.Http;
using AdyenApIs;
using AdyenApIs.Core.Configuration;
using AdyenApIs.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

public static class AdyenServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient the Adyen SDK client is built over (kept off the shared default client).</summary>
    public const string HttpClientName = "Adyen";

    // Per attempt. Retries are off (both writes are POSTs), so with the gateway's call budget a hung
    // provider costs at most one attempt per call.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Environment variables the Adyen credentials are commonly provisioned as, mapped onto the
    /// <c>Adyen:</c> section at the lowest precedence (any other source wins).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> EnvironmentVariableMap = new Dictionary<string, string>
    {
        ["ADYEN_API_KEY"] = "Adyen:ApiKey",
        ["ADYEN_MERCHANT_ACCOUNT"] = "Adyen:MerchantAccount",
        ["ADYEN_ENVIRONMENT"] = "Adyen:Environment",
        ["ADYEN_CURRENCY"] = "Adyen:Currency",
    };

    /// <summary>
    /// Adds the <c>ADYEN_*</c> environment variables as the lowest-precedence configuration source for the
    /// matching <c>Adyen:</c> keys, so user-secrets, appsettings and <c>Adyen__*</c> variables still override them.
    /// </summary>
    public static IConfigurationBuilder AddAdyenEnvironmentVariableFallback(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentVariableMap)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
        }

        builder.Sources.Insert(0, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = values });
        return builder;
    }

    /// <summary>
    /// Registers the Adyen-backed payment gateway, the payment store and the order payment service.
    /// Settings are validated when the host starts: a missing credential stops the app from booting.
    /// </summary>
    public static IServiceCollection AddAdyenPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdyenSettings>()
            .Bind(configuration.GetSection(AdyenSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (string.IsNullOrWhiteSpace(settings.ReturnUrl))
                    settings.ReturnUrl = configuration["baseUrls:webBase"];
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdyenSettings>, AdyenSettingsValidator>();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop per attempt; the gateway's per-call deadline is the real bound.
                client.Timeout = AdyenPaymentGateway.CallBudget;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton: recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One long-lived SDK client. Options (including the API key) are captured here once, so a rotated
        // key takes effect on the next restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AdyenSettings>>().Value;
            var options = new AdyenApIsClientOptions
            {
                Environment = ServerEnvironment.Production,
                ApiKeyAuth = settings.ApiKey,
                Retry = RetryOptions.Disabled() with { Timeout = AttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Assigned explicitly so the SDK's log environment variable can never switch on body
                    // logging: payment request bodies carry (encrypted) card data and the holder's name.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System,
            };

            // The SDK's only environment targets Adyen's test endpoints; live traffic requires the merchant's
            // live Checkout URL explicitly (validated at startup).
            if (!string.IsNullOrWhiteSpace(settings.CheckoutBaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.CheckoutBaseUrl.TrimEnd('/');
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new AdyenApIsClient(httpClient, options);
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPaymentGateway, AdyenPaymentGateway>();
        services.AddScoped<IOrderPaymentStore, OrderPaymentStore>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }
}
