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
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddTransient<PayPalStatusCaptureHandler>();
        services.AddTransient<PayPalRequestLoggingHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .AddHttpMessageHandler<PayPalRequestLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var paypal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            return new PayPalServerSdkClient(httpClient, BuildClientOptions(paypal));
        });

        services.AddSingleton<IPaymentSettings, ConfigurePaymentSettings>();
        services.AddScoped<IPayPalPaymentGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    internal static PayPalServerSdkClientOptions BuildClientOptions(PayPalOptions paypal)
    {
        if (!string.IsNullOrWhiteSpace(paypal.Environment)
            && !string.Equals(paypal.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PayPal:Environment '{paypal.Environment}' is not supported. This SDK only exposes Sandbox.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
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

        if (!string.IsNullOrWhiteSpace(paypal.ClientId))
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = paypal.ClientId,
                ClientSecret = paypal.ClientSecret ?? string.Empty
            };
        }

        return options;
    }
}
