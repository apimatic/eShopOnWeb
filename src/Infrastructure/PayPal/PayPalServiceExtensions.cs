using System;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceExtensions
{
    public static IServiceCollection AddPayPalPaymentService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection("PayPal").Get<PayPalSettings>() ?? new PayPalSettings();
        configuration.GetSection("PayPal").Bind(settings);

        services.Configure<PayPalSettings>(configuration.GetSection("PayPal"));

        const string clientName = "PayPalSdk";

        services.AddHttpClient(clientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(clientName);

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
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(25)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentService>(sp =>
        {
            var client = sp.GetRequiredService<PayPalServerSdkClient>();
            var currency = settings.Currency;
            return new PayPalPaymentService(client, currency);
        });

        return services;
    }
}
