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

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Wires the PayPal SDK client, the payment gateway, and the payment/order services. All settings
    /// come from the <c>PayPal:</c> configuration section — nothing is hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.ConfigSection));

        // A named HttpClient keeps the SDK's pipeline off the shared default client, with an explicit
        // timeout (bounds one attempt) and a pooled-connection lifetime (the SDK client is a singleton,
        // so this keeps DNS fresh behind it).
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
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
                // This SDK build exposes only the Sandbox environment; a non-sandbox host is reached
                // solely via the explicit BaseUrl override below.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            // When PayPal:BaseUrl is set, use it verbatim for every call — including the token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = settings.BaseUrl }
                    }
                };
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentConfiguration, PayPalPaymentConfiguration>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
