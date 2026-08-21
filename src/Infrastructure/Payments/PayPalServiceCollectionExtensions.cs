using System;
using System.Collections.Generic;
using System.Net.Http;
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
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler(() => new LastStatusHandler())
            .AddHttpMessageHandler(() => new RefuseUnauthorizedResendHandler())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var paypal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
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
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(paypal.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = paypal.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();
        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }

    public static IConfigurationBuilder AddPayPalEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>();
        Copy(data, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Copy(data, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Copy(data, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Copy(data, "PAYPAL_CURRENCY", "PayPal:Currency");
        Copy(data, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }

        return builder;
    }

    private static void Copy(IDictionary<string, string?> data, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configKey] = value;
        }
    }
}
