using System;
using System.Net.Http;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPalPayments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PayPal;
using PayPal.Core.Authentication.OAuth2.ClientCredentials;
using PayPal.Core.Configuration;
using PayPal.Servers;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PayPalClientRegistration
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, PayPalOptions payPalOptions)
    {
        services.AddSingleton(payPalOptions);

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var options = new PayPalClientOptions
            {
                Environment = ServerEnvironment.Production,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = payPalOptions.ClientId,
                    ClientSecret = payPalOptions.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = payPalOptions.BaseUrl;
            }

            return new PayPalClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalCheckoutGateway>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        return services;
    }
}
