using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
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
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .PostConfigure(opts =>
            {
                if (string.IsNullOrWhiteSpace(opts.ClientId))
                {
                    opts.ClientId = configuration["PAYPAL_CLIENT_ID"] ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(opts.ClientSecret))
                {
                    opts.ClientSecret = configuration["PAYPAL_CLIENT_SECRET"] ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(opts.Environment))
                {
                    opts.Environment = configuration["PAYPAL_ENVIRONMENT"] ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(opts.Currency))
                {
                    opts.Currency = configuration["PAYPAL_CURRENCY"] ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(opts.BaseUrl))
                {
                    opts.BaseUrl = configuration["PAYPAL_BASE_URL"];
                }
            });

        services.AddTransient<PayPalWriteOnceHandler>();
        services.AddTransient<PayPalStatusCaptureHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalWriteOnceHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var paypal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            if (string.IsNullOrWhiteSpace(paypal.ClientId) || string.IsNullOrWhiteSpace(paypal.ClientSecret))
            {
                throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
            }

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = paypal.ClientId,
                    ClientSecret = paypal.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                }
            };

            if (!string.IsNullOrWhiteSpace(paypal.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = paypal.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPayPalSettings, PayPalSettings>();
        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        return services;
    }
}
