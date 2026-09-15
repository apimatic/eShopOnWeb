using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
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

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();

        services.AddTransient<PayPalWriteOnceHandler>();
        services.AddTransient<PayPalStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalWriteOnceHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            Validate(settings);

            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl.TrimEnd('/');
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentsGateway, PayPalPaymentsGateway>();
        services.AddScoped<IOrderCheckoutService, OrderCheckoutService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();

        return services;
    }

    private static void Validate(PayPalOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId and PayPal:ClientSecret must be configured (environment variables PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET or user secrets).");
        }

        if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This integration supports only the PayPal sandbox. Set PayPal:Environment (PAYPAL_ENVIRONMENT) to sandbox.");
        }

        if (string.IsNullOrWhiteSpace(settings.Currency) || settings.Currency.Trim().Length != 3)
        {
            throw new InvalidOperationException(
                "PayPal:Currency must be a 3-letter ISO-4217 code (environment variable PAYPAL_CURRENCY).");
        }
    }
}
