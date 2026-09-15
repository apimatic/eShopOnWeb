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
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddTransient<LastStatusHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<LastStatusHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            Validate(options);
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, CreateSdkOptions(options));
        });

        services.AddScoped<IPaymentProcessor, PayPalPaymentProcessor>();
        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();
        return services;
    }

    internal static void Validate(PayPalOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
        if (string.IsNullOrWhiteSpace(options.Currency))
            throw new InvalidOperationException("PayPal:Currency must be configured.");
        if (!IsSandbox(options.Environment))
            throw new InvalidOperationException("This PayPal SDK only supports the sandbox environment. Set PayPal:Environment to sandbox.");
    }

    internal static bool IsSandbox(string? environment) =>
        string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Sandbox", StringComparison.Ordinal);

    internal static PayPalServerSdkClientOptions CreateSdkOptions(PayPalOptions options)
    {
        var sdkOptions = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1
            }
        };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            sdkOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl;

        return sdkOptions;
    }
}
