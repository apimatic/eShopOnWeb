using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal payment gateway. Assumes <see cref="PayPalSettings"/> is already bound
/// (the host calls <c>services.Configure&lt;PayPalSettings&gt;(config.GetSection("PayPal"))</c> first).
/// </summary>
public static class PaymentDependencies
{
    // A named HttpClient keeps this SDK's timeout/handler off the shared default (unnamed) client.
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services)
    {
        // IHttpClientFactory-managed, long-lived pipeline. Timeout bounds one attempt (a hang);
        // PooledConnectionLifetime keeps DNS fresh behind the singleton client below.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        // The SDK client is lightweight controller wrappers over the shared pipeline — register it once.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // Sandbox is the only environment this SDK build exposes; an explicit BaseUrl (below)
                // is what actually redirects both API calls AND the OAuth token request.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                },
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim for every call, including /v1/oauth2/token.
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
