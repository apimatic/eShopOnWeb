using System.Net.Http;
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

public static class PayPalClientRegistration
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));

        services.AddTransient<PayPalOnceWriteHandler>();
        services.AddTransient<PayPalStatusCaptureHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<PayPalOnceWriteHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId ?? string.Empty,
                    ClientSecret = settings.ClientSecret ?? string.Empty
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl.TrimEnd('/');
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();
        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IShopOrderService, ShopOrderService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        return services;
    }
}
