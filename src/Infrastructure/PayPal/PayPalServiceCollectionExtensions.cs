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

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds the <c>PayPal</c> configuration section and registers the PayPal SDK client (long-lived, over a
    /// dedicated named HttpClient with a bounded timeout and pooled-connection rotation) plus the
    /// <see cref="IPaymentGateway"/> that fronts it.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // A dedicated named HttpClient keeps this pipeline off the shared default client. The per-attempt
        // timeout bounds a hung provider; PooledConnectionLifetime keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived (lightweight controller wrappers over the shared HTTP pipeline).
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
                // Bound one attempt; the whole-call budget is the request's CancellationToken.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) }
            };

            // Optional explicit base URL: when set, used verbatim for every PayPal call (incl. the token request).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentConfiguration, PaymentConfigurationAdapter>();

        return services;
    }
}
