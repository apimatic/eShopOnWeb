using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
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
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services)
    {
        services.AddSingleton<PayPalLastStatusHandler>();
        services.AddSingleton<PayPalAtMostOneWriteHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = AttemptTimeout;
            })
            .AddHttpMessageHandler<PayPalLastStatusHandler>()
            .AddHttpMessageHandler<PayPalAtMostOneWriteHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            return new PayPalServerSdkClient(httpClient, BuildOptions(settings));
        });

        services.AddSingleton<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();
        return services;
    }

    private static PayPalServerSdkClientOptions BuildOptions(PayPalOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.Currency) || settings.Currency.Length != 3)
        {
            throw new InvalidOperationException("PayPal:Currency must be a 3-letter ISO-4217 code.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout,
                MaxRetries = 1
            },
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Environment)
            && !string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This PayPal SDK only supports the sandbox environment.");
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}
