using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    private const string ClientName = "PayPal";

    /// <summary>
    /// Binds the "PayPal" settings, constructs a long-lived <see cref="PayPalServerSdkClient"/> over a
    /// named, pooled <see cref="HttpClient"/> (with OAuth2 client-credentials and the optional base-URL
    /// override applied to every call including the token request), and registers the gateway and the
    /// payment application service.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        // A named, pooled HttpClient keeps this pipeline off the shared default client and keeps DNS
        // fresh behind the long-lived (singleton) SDK client. A bounded timeout stops a hung provider
        // from pinning a request thread.
        services.AddHttpClient(ClientName, c => c.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<PayPalSettings>();

            // The SDK exposes only the Sandbox environment node. A non-sandbox host is reachable only via
            // the explicit base-URL override; refuse silently targeting production without one.
            if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                throw new InvalidOperationException(
                    "The PayPal SDK exposes only the sandbox environment. To target a non-sandbox host, set PayPal:BaseUrl.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            // When set, the override is used verbatim as the API base address for every call — the SDK
            // builds the OAuth token URL from this same node, so it reaches the token request too.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
