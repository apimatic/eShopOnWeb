using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PayPalClientRegistration
{
    /// <summary>
    /// Registers the PayPal SDK client (singleton over one long-lived HttpClient), the payment
    /// gateway and the payment application service. Settings come from the "PayPal" section;
    /// when BaseUrl is set it is used verbatim for every PayPal call, including the OAuth
    /// token request.
    /// </summary>
    public static IServiceCollection AddPayPalServices(this IServiceCollection services, PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. via user-secrets from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).");
        }
        if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PayPal environment '{settings.Environment}' is not supported by this build; only 'sandbox' is available.");
        }
        if (string.IsNullOrWhiteSpace(settings.Currency))
        {
            throw new InvalidOperationException("PayPal:Currency must be configured (e.g. from PAYPAL_CURRENCY).");
        }

        services.AddSingleton(settings);

        services.AddSingleton(_ =>
        {
            var httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
