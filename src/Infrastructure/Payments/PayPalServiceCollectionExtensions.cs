using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers PayPal payment services: settings + fail-fast validation, a long-lived SDK client over a
/// dedicated named <see cref="HttpClient"/>, and the <see cref="IPayPalPaymentGateway"/> implementation.
/// Called from the PublicApi host only (not the shared Infrastructure registration) so other hosts are not
/// forced to carry PayPal credentials.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPalServerSdk";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PayPalSettings>, PayPalSettingsValidator>();

        // A dedicated HttpClient keeps the SDK's pipeline off the shared default client. The per-attempt
        // timeout is a backstop; the whole-call budget is enforced by the gateway's CancellationToken.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(100);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Recycle pooled connections so a long-lived singleton client keeps DNS fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived (its OAuth token cache lives on it). Options are captured once at
        // registration; a rotated secret takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!
                },
                // Assign LoggerFactory explicitly and keep LogRequestBody off, so card data is never logged
                // and the SDK's log environment variable cannot switch body logging on from outside the code.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional base-URL override: applied to every call, including the OAuth token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();

        // Currency for the payment flow, sourced once from PayPal:Currency (single source of truth).
        services.AddSingleton(sp => new PaymentOptions
        {
            Currency = sp.GetRequiredService<IOptions<PayPalSettings>>().Value.Currency!
        });

        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }
}
