using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal SDK client (singleton over a named, factory-managed HttpClient)
    /// and the IPaymentGateway implementation. Settings bind from the "PayPal" section.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the SDK's per-attempt retry timeout is set below too.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK declares only a Sandbox environment; production is reached via BaseUrl override.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            var baseUrl = settings.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl)
                && !string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://api-m.paypal.com";
            }
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                // Covers every call including the OAuth token request.
                options.Server.Default.Sandbox.BaseUrl = baseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }
}
