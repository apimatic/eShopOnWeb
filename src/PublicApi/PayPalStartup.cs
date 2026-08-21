using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi;

public static class PayPalStartup
{
    public const string HttpClientName = "PayPal";

    public static void OverlayPayPalEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>();
        Map(overlay, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Map(overlay, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Map(overlay, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Map(overlay, "PAYPAL_CURRENCY", "PayPal:Currency");
        Map(overlay, "PAYPAL_BASE_URL", "PayPal:BaseUrl");
        if (overlay.Count > 0)
        {
            builder.AddInMemoryCollection(overlay);
        }
    }

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddTransient<LastStatusHandler>();
        services.AddTransient<SingleSendHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<LastStatusHandler>()
            .AddHttpMessageHandler<SingleSendHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
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

        services.AddSingleton<IPaymentConfiguration, PaymentConfiguration>();
        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        return services;
    }

    private static void Map(IDictionary<string, string?> overlay, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configKey] = value;
        }
    }
}
