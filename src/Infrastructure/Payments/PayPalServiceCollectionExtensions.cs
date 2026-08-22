using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
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
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        services.AddTransient<PayPalHttpStatusHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalHttpStatusHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<PayPalSettings>();
            return new PayPalServerSdkClient(httpClient, CreateOptions(settings));
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }

    internal static PayPalServerSdkClientOptions CreateOptions(PayPalSettings settings)
    {
        var environment = settings.Environment?.Trim();
        if (!string.IsNullOrEmpty(environment) &&
            !string.Equals(environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PayPal:Environment must be Sandbox. This SDK build exposes only ServerEnvironment.Sandbox.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId ?? string.Empty,
                ClientSecret = settings.ClientSecret ?? string.Empty,
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

        return options;
    }
}
