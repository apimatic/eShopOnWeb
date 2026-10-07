using System;
using System.Collections.Generic;
using System.Net.Http;
using AdyenApIs;
using AdyenApIs.Core.Configuration;
using AdyenApIs.Servers;
using BlazorShared;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

public static class AdyenServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient reserved for Adyen, so its timeout and handler never leak to other clients.</summary>
    public const string HttpClientName = "Adyen";

    private static readonly Dictionary<string, string> EnvironmentVariableKeys = new()
    {
        ["ADYEN_API_KEY"] = $"{AdyenSettings.SectionName}:ApiKey",
        ["ADYEN_MERCHANT_ACCOUNT"] = $"{AdyenSettings.SectionName}:MerchantAccount",
        ["ADYEN_ENVIRONMENT"] = $"{AdyenSettings.SectionName}:Environment",
        ["ADYEN_CURRENCY"] = $"{AdyenSettings.SectionName}:Currency",
    };

    /// <summary>
    /// Maps the ADYEN_* environment variables a deployment provides onto the <c>Adyen:</c> keys. Added last, so an
    /// account supplied through the environment wins over user-secrets; variables that are unset are skipped.
    /// </summary>
    public static IConfigurationBuilder AddAdyenEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentVariableKeys)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                values[key] = value;
        }
        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }

    /// <summary>
    /// Registers the Adyen client and the card payment gateway. The host refuses to start when the <c>Adyen:</c>
    /// settings are incomplete. Settings are read once, when the client is first built: rotating the API key takes
    /// a restart.
    /// </summary>
    public static IServiceCollection AddAdyenPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdyenSettings>()
            .Bind(configuration.GetSection(AdyenSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (string.IsNullOrWhiteSpace(settings.ReturnUrl))
                {
                    var webBase = configuration[$"{BaseUrlConfiguration.CONFIG_NAME}:{nameof(BaseUrlConfiguration.WebBase)}"];
                    if (!string.IsNullOrWhiteSpace(webBase))
                        settings.ReturnUrl = webBase.TrimEnd('/') + "/order/my-orders";
                }
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdyenSettings>, AdyenSettingsValidator>();

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                // Backstop above the SDK's own per-call timeout; POSTs are never retried, so this bounds a hang.
                httpClient.Timeout = sp.GetRequiredService<IOptions<AdyenSettings>>().Value.Timeout + TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; recycle connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AdyenSettings>>().Value;
            var options = new AdyenApIsClientOptions
            {
                // The SDK's only environment; its Checkout server is Adyen's test host (Adyen:Environment = test).
                Environment = ServerEnvironment.Production,
                ApiKeyAuth = settings.ApiKey,
                // Payments and refunds are never re-sent by the SDK. The only re-send is ours, with the same
                // idempotency key, when an outcome is unknown.
                Retry = RetryOptions.Disabled() with { Timeout = settings.Timeout },
                // Explicit logger factory (also disables the ADYENAPISCLIENT_LOG switch); never log bodies or headers:
                // requests carry encrypted card data and the holder's name.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System,
            };
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new AdyenApIsClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, AdyenPaymentGateway>();
        return services;
    }
}
