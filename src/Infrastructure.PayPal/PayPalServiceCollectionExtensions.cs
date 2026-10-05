using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>Named HttpClient carrying only PayPal traffic (its own timeout and handler).</summary>
    public const string HttpClientName = "PayPal";

    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableFallbacks = new Dictionary<string, string>
    {
        ["PayPal:ClientId"] = "PAYPAL_CLIENT_ID",
        ["PayPal:ClientSecret"] = "PAYPAL_CLIENT_SECRET",
        ["PayPal:Environment"] = "PAYPAL_ENVIRONMENT",
        ["PayPal:Currency"] = "PAYPAL_CURRENCY",
    };

    /// <summary>
    /// Fills any <c>PayPal:</c> key that no other source supplied from its conventional environment
    /// variable (PAYPAL_CLIENT_ID, …). Values that are already configured are left untouched.
    /// </summary>
    public static IConfigurationManager AddPayPalEnvironmentVariableFallback(this IConfigurationManager configuration)
    {
        var fallbacks = new Dictionary<string, string?>();
        foreach (var (key, variable) in EnvironmentVariableFallbacks)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(configuration[key]) && !string.IsNullOrWhiteSpace(value))
                fallbacks[key] = value;
        }
        if (fallbacks.Count > 0)
            configuration.AddInMemoryCollection(fallbacks);
        return configuration;
    }

    /// <summary>
    /// Registers the PayPal-backed <see cref="IPaymentGateway"/>: options bound from <c>PayPal:</c> and
    /// validated at startup, one long-lived SDK client (it owns the OAuth token cache), and a per-request
    /// time budget.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>());
        services.TryAddSingleton(new PayPalResilienceSettings());
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, http) => http.Timeout = sp.GetRequiredService<PayPalResilienceSettings>().HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var resilience = sp.GetRequiredService<PayPalResilienceSettings>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            // Built once: a rotated secret takes effect on restart.
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!.Trim(),
                    ClientSecret = settings.ClientSecret!.Trim(),
                },
                Retry = RetryOptions.Default() with
                {
                    // Only reads are ever resent by the SDK; writes are settled by our own code.
                    HttpMethodsToRetry = [HttpMethod.Get],
                    MaxRetries = resilience.MaxReadRetries,
                    Timeout = resilience.AttemptTimeout,
                },
                Logging = new LoggingOptions
                {
                    // Set explicitly so the SDK's log environment variable can never switch body logging on:
                    // request bodies carry card numbers.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                TimeProvider = sp.GetRequiredService<TimeProvider>(),
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim for every call, including the OAuth token request (it resolves through the same server).
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl.Trim();
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<PayPalCallBudget>();
        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }
}
