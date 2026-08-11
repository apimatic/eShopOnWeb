using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string LiveBaseUrl = "https://api-m.paypal.com";
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds PayPal settings from the <c>PayPal:</c> configuration section, constructs a long-lived
    /// PayPal SDK client over a named <see cref="HttpClient"/>, and registers the payment gateway.
    /// Credential values come only from configuration (user-secrets / environment).
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // A named HttpClient keeps the SDK's pipeline (timeout, primary handler) off the shared
        // default client. A per-attempt timeout backstops a hung provider; a bounded pooled
        // connection lifetime keeps DNS fresh behind the long-lived (singleton) client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET) via user-secrets or environment.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK exposes only a Sandbox environment; Live and any custom host are reached by
                // overriding the base URL below (which also redirects the OAuth token request).
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            var overrideUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl)
                ? settings.BaseUrl!.Trim()
                : settings.IsLive ? LiveBaseUrl : null;

            if (overrideUrl is not null)
            {
                options.Server.Default.Sandbox.BaseUrl = overrideUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentSettings, PaymentSettingsAdapter>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
