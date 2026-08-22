using System;
using System.Net.Http;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi;

public static class PayPalServiceExtensions
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.PostConfigure<PayPalSettings>(settings =>
        {
            settings.ClientId = FirstNonEmpty(settings.ClientId, Environment.GetEnvironmentVariable("PAYPAL_CLIENT_ID"));
            settings.ClientSecret = FirstNonEmpty(settings.ClientSecret, Environment.GetEnvironmentVariable("PAYPAL_CLIENT_SECRET"));
            settings.Environment = FirstNonEmpty(settings.Environment, Environment.GetEnvironmentVariable("PAYPAL_ENVIRONMENT"));
            settings.Currency = FirstNonEmpty(settings.Currency, Environment.GetEnvironmentVariable("PAYPAL_CURRENCY"));
            settings.BaseUrl = FirstNonEmpty(settings.BaseUrl, Environment.GetEnvironmentVariable("PAYPAL_BASE_URL"));
        });

        services.AddTransient<PayPalStatusCaptureHandler>();
        services.AddTransient<PayPalSafeLoggingHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalSafeLoggingHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId ?? string.Empty,
                    ClientSecret = settings.ClientSecret ?? string.Empty
                }
            };

            var baseUrl = settings.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl) && IsLiveEnvironment(settings.Environment))
                baseUrl = "https://api-m.paypal.com";
            if (!string.IsNullOrWhiteSpace(baseUrl))
                options.Server.Default.Sandbox.BaseUrl = baseUrl;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPayPalConfiguration, ConfigurePayPalSettings>();
        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<ICheckoutPaymentService, CheckoutPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        return services;
    }

    private static bool IsLiveEnvironment(string? environment) =>
        string.Equals(environment, "Live", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return string.Empty;
    }
}
