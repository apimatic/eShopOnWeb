using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));

        services.AddTransient<PayPalLoggingHandler>();
        services.AddTransient<PayPalWriteOnceHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalLoggingHandler>()
            .AddHttpMessageHandler<PayPalWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            Validate(options);

            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                },
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret
                }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }

    private static void Validate(PayPalOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are missing. Set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).");
        }

        if (string.IsNullOrWhiteSpace(options.Currency) || options.Currency.Length != 3)
        {
            throw new InvalidOperationException("PayPal:Currency must be a 3-letter ISO-4217 code (from PAYPAL_CURRENCY).");
        }

        if (!string.Equals(options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This SDK build only supports the PayPal sandbox. Set PayPal:Environment to sandbox (from PAYPAL_ENVIRONMENT).");
        }
    }
}
