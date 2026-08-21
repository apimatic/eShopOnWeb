using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.AddSingleton<IPaymentSettings, ConfigurePayPalPaymentSettings>();

        services.AddTransient<PayPalRequestLoggingHandler>();
        services.AddTransient<PayPalSingleWriteHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<PayPalSingleWriteHandler>()
            .AddHttpMessageHandler<PayPalRequestLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            ValidateSettings(settings);

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
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
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IShopOrderService, ApplicationCore.Services.ShopOrderService>();
        services.AddScoped<IOrderPaymentService, ApplicationCore.Services.OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, ApplicationCore.Services.SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ApplicationCore.Services.ReconciliationService>();

        return services;
    }

    private static void ValidateSettings(PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.Currency))
        {
            throw new InvalidOperationException("PayPal:Currency must be configured.");
        }

        if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.Environment))
        {
            throw new InvalidOperationException(
                "This SDK build supports only the PayPal sandbox environment. PayPal:Environment must be 'sandbox'.");
        }
    }
}
