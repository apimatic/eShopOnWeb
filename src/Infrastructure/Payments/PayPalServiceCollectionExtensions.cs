using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
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
        services.AddSingleton<IOptions<PayPalOptions>>(_ =>
        {
            var section = configuration.GetSection(PayPalOptions.SectionName);
            return Options.Create(new PayPalOptions
            {
                ClientId = section["ClientId"] ?? string.Empty,
                ClientSecret = section["ClientSecret"] ?? string.Empty,
                Environment = section["Environment"] ?? string.Empty,
                Currency = section["Currency"] ?? string.Empty,
                BaseUrl = section["BaseUrl"]
            });
        });
        services.AddSingleton<IPaymentSettings, ConfigurePaymentSettings>();
        services.AddTransient<SingleWriteHandler>();
        services.AddTransient<PayPalStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<SingleWriteHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var paypal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            return new PayPalServerSdkClient(httpClient, CreateClientOptions(paypal));
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        return services;
    }

    internal static PayPalServerSdkClientOptions CreateClientOptions(PayPalOptions paypal)
    {
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(10)
            },
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = paypal.ClientId,
                ClientSecret = paypal.ClientSecret
            }
        };

        if (!string.IsNullOrWhiteSpace(paypal.BaseUrl))
            options.Server.Default.Sandbox.BaseUrl = paypal.BaseUrl;

        return options;
    }
}
