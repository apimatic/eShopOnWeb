using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class PayPalServiceExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds <see cref="PayPalSettings"/> from the <c>PayPal:</c> section (failing startup if a
    /// credential is missing/blank), registers the PayPal Server SDK client over a dedicated named
    /// HttpClient, and wires the payment gateway and application services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to boot when a required credential is missing or blank, rather than
        // discovering it as a 401 on the first call. [Required] rejects null and empty.
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.CONFIG_SECTION))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A dedicated HttpClient so this SDK's timeout/handler don't touch the shared default client.
        // Timeout bounds one attempt (backstop); PooledConnectionLifetime keeps DNS fresh behind the
        // long-lived singleton client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Build the SDK client once at registration (options captured in the singleton; a rotated
        // secret takes effect on restart). LoggerFactory is set explicitly and LogRequestBody stays
        // off, so card PANs can never be logged and the PAYPALSERVERSDKCLIENT_LOG env var is disarmed.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                    LogRequestBody = false
                }
            };

            // Optional override: when set, use it verbatim as the base for EVERY call, incl. the token
            // request (the token URL resolves through this same server base).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentGateway, PayPalGateway>();

        services.AddScoped<IOrderCheckoutService, OrderCheckoutService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
