using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers PayPal payment support: strongly-typed settings with startup fail-fast, the PayPal
    /// SDK client (singleton over a named HttpClient), the payment processor, and the application
    /// payment services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind + fail-fast at startup: refuse to boot if any required value is missing/blank, so a
        // misconfiguration surfaces here and not as a 401 on the first live call. The value is never
        // echoed, only the missing configuration key is named.
        var settings = BindSettings(configuration);
        ValidateOrThrow(settings);

        services.AddSingleton(settings);

        // Provider-agnostic payment options for ApplicationCore (currency).
        services.AddSingleton(new PaymentOptions { CurrencyCode = settings.Currency });

        // Ensure the eShop logging adapter is available for the payment services.
        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));

        // Named HttpClient: bound per-attempt timeout + pooled-connection recycling behind the
        // long-lived singleton client (keeps DNS fresh).
        services.AddHttpClient(HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.PerAttemptTimeoutSeconds));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Per-attempt timeout on the SDK retry pipeline (a whole-call budget is enforced by
                // the processor via a CancellationToken deadline).
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.PerAttemptTimeoutSeconds))
                },
                // Assign the logger explicitly so the PAYPALSERVERSDKCLIENT_LOG environment variable
                // cannot switch on request-body logging (card data) from outside the code.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional base-URL override: when set, used verbatim for every call (including the
            // OAuth token request, which resolves against this Default-group base URL).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentProcessor, PayPalPaymentProcessor>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }

    private static PayPalSettings BindSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        var settings = new PayPalSettings
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? string.Empty,
            Currency = section["Currency"] ?? string.Empty,
            BaseUrl = section["BaseUrl"]
        };
        if (int.TryParse(section["TotalTimeoutSeconds"], out var total)) settings.TotalTimeoutSeconds = total;
        if (int.TryParse(section["PerAttemptTimeoutSeconds"], out var perAttempt))
            settings.PerAttemptTimeoutSeconds = perAttempt;
        return settings;
    }

    private static void ValidateOrThrow(PayPalSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ClientId)) missing.Add("PayPal:ClientId");
        if (string.IsNullOrWhiteSpace(settings.ClientSecret)) missing.Add("PayPal:ClientSecret");
        if (string.IsNullOrWhiteSpace(settings.Environment)) missing.Add("PayPal:Environment");
        if (string.IsNullOrWhiteSpace(settings.Currency)) missing.Add("PayPal:Currency");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"PayPal configuration is incomplete: {string.Join(", ", missing)} is not configured. " +
                "Set it via user-secrets or environment before starting the app.");
    }
}
