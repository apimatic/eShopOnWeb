using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var options = BindOptions(configuration);
        services.AddSingleton(options);

        services.AddTransient<PayPalWriteOnceHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<PayPalOptions>();
            var clientOptions = new PayPalServerSdkClientOptions
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
                clientOptions.Server.Default.Sandbox.BaseUrl = settings.BaseUrl.Trim().TrimEnd('/');
            }

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        return services;
    }

    public static PayPalOptions BindOptions(IConfiguration configuration)
    {
        var bound = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>() ?? new PayPalOptions();
        bound.ClientId = FirstNonEmpty(bound.ClientId, configuration["PAYPAL_CLIENT_ID"]);
        bound.ClientSecret = FirstNonEmpty(bound.ClientSecret, configuration["PAYPAL_CLIENT_SECRET"]);
        bound.Environment = FirstNonEmpty(bound.Environment, configuration["PAYPAL_ENVIRONMENT"]);
        bound.Currency = FirstNonEmpty(bound.Currency, configuration["PAYPAL_CURRENCY"]);
        bound.BaseUrl = FirstNonEmpty(bound.BaseUrl, configuration["PayPal:BaseUrl"], configuration["PAYPAL_BASE_URL"]);
        return bound;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
