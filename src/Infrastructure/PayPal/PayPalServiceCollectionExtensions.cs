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
    private const string HttpClientName = "PayPalServerSdk";

    /// <summary>
    /// Registers the PayPal integration: fail-fast settings binding, a long-lived SDK client over a named
    /// <see cref="System.Net.Http.HttpClient"/>, and the <see cref="IPayPalGateway"/> port. Credentials come
    /// from the bound <c>PayPal:</c> section (user-secrets/env) and are captured once at registration.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: every credential is checked non-blank (a blank part is not a missing one), and the host
        // refuses to boot when one is absent — rather than discovering it as a 401 on the first call. The
        // messages name the config key and never echo a value.
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId is not configured. Set it via user-secrets or environment (PAYPAL_CLIENT_ID).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret is not configured. Set it via user-secrets or environment (PAYPAL_CLIENT_SECRET).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Environment), "PayPal:Environment is not configured. Set it via user-secrets or environment (PAYPAL_ENVIRONMENT).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency is not configured. Set it via user-secrets or environment (PAYPAL_CURRENCY).")
            .Validate(
                s => s.IsSandbox || !string.IsNullOrWhiteSpace(s.BaseUrl),
                "PayPal:Environment is not 'sandbox' but PayPal:BaseUrl is not set. The SDK declares only a " +
                "Sandbox environment, so a non-sandbox target must set PayPal:BaseUrl to the correct base address.")
            .ValidateOnStart();

        // Named HttpClient so the timeout/handler pipeline is scoped to this SDK (not the shared default client).
        // PooledConnectionLifetime keeps DNS fresh behind the long-lived singleton client below.
        services.AddHttpClient(HttpClientName, c =>
            {
                // Per-attempt transport backstop; the real caller-visible budget is the gateway's own
                // CancellationToken deadline (PayPal:TimeoutSeconds), which cancels a whole call sooner.
                c.Timeout = TimeSpan.FromSeconds(60);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK declares only Sandbox; a non-sandbox host is reached via the BaseUrl override below.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                },
                // Bound each attempt to the same budget; our CancellationToken bounds the whole call.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) },
                // Explicitly own the logger so the PAYPALSERVERSDKCLIENT_LOG env var cannot switch on
                // unredacted request-body logging from outside the code. Bodies (card data) are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim as the base address for EVERY call, including the OAuth token request
                // (the token URL resolves through this same server base).
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPayPalGateway, PayPalGateway>();

        return services;
    }
}
