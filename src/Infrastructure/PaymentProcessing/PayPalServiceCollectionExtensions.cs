using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

public static class PayPalServiceCollectionExtensions
{
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    public static IServiceCollection AddPayPalPaymentProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var payPalOptions = new PayPalOptions();
        configuration.GetSection("PayPal").Bind(payPalOptions);
        services.AddSingleton(payPalOptions);

        var paymentSettings = new PaymentSettings { Currency = payPalOptions.Currency };
        services.AddSingleton(paymentSettings);

        // AddPayPalServerSdkClient resolves the default, unnamed IHttpClientFactory client and
        // builds a Singleton PayPalServerSdkClient from it - tune that default client's timeout and
        // connection lifetime here, before the SDK's own registration call below.
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddPayPalServerSdkClient(o =>
        {
            o.Environment = ServerEnvironment.Sandbox;
            o.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = payPalOptions.ClientId,
                ClientSecret = payPalOptions.ClientSecret
            };
            o.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };

            if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
            {
                o.Server.Default.Sandbox.BaseUrl = payPalOptions.BaseUrl;
            }
            else if (string.Equals(payPalOptions.Environment, "live", StringComparison.OrdinalIgnoreCase))
            {
                o.Server.Default.Sandbox.BaseUrl = LiveBaseUrl;
            }
        });

        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
