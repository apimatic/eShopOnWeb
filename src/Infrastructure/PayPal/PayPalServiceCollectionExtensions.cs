using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds <see cref="PayPalSettings"/> from the <c>PayPal:</c> section (fail-fast at startup), builds and
    /// registers a single long-lived <see cref="PayPalServerSdkClient"/> over a named, pooled HttpClient,
    /// and registers the <see cref="IPayPalPaymentGateway"/>.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            // Fail-fast: every credential part is checked, and a blank part is treated as missing. The
            // message names the config key and never echoes a value.
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Environment), "PayPal:Environment is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency) && s.Currency.Length == 3,
                "PayPal:Currency is not configured or is not a 3-letter ISO-4217 code.")
            .Validate(s =>
                    // Only "sandbox" has a server member in this SDK. Any other environment with no explicit
                    // BaseUrl override would silently fall back to the sandbox host — reject it so test/live
                    // traffic can never be misrouted.
                    string.Equals(s.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(s.BaseUrl),
                "PayPal:Environment must be 'sandbox' (the only environment this SDK supports) unless PayPal:BaseUrl is set to an explicit host.")
            .ValidateOnStart();

        // Expose the validated settings object directly for constructor injection.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        // A dedicated, pooled HttpClient for the SDK — kept off the shared default client. PooledConnectionLifetime
        // keeps DNS fresh behind the long-lived singleton client; Timeout is a per-attempt backstop under the
        // whole-call CancellationToken deadline the gateway applies.
        services.AddHttpClient(HttpClientName, (sp, c) =>
            {
                var settings = sp.GetRequiredService<PayPalSettings>();
                c.Timeout = TimeSpan.FromSeconds(settings.CallTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<PayPalSettings>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                },
                // Card PANs/CVVs travel in request bodies, so logging posture is deliberate: assign the
                // LoggerFactory explicitly (disarms the PAYPALSERVERSDKCLIENT_LOG env var) and keep request
                // bodies out of the logs.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                // Per-attempt timeout aligned with the call budget; the gateway's CancellationToken bounds the
                // whole call. Writes are POST/DELETE, which the SDK never resends by default.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(settings.CallTimeoutSeconds) },
            };

            // Optional base-URL override — used verbatim for every call (including the token request) when set.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentConfiguration, PayPalPaymentConfiguration>();

        // Application services orchestrating the flows (scoped — they use scoped EF repositories).
        services.AddScoped<IPaymentOrchestrationService, Payments.PaymentOrchestrationService>();
        services.AddScoped<ISavedCardService, Payments.SavedCardService>();

        return services;
    }
}
