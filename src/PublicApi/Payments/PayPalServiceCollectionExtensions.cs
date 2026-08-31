using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string ClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddTransient<PayPalWriteGuardHandler>();
        services.AddHttpClient(ClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .AddHttpMessageHandler<PayPalWriteGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var options = new PayPalServerSdk.PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
            return new PayPalServerSdk.PayPalServerSdkClient(httpClient, options);
        });
        services.AddSingleton<IPayPalGateway, PayPalGateway>();
        services.AddSingleton<PaymentOperationLock>();
        services.AddScoped<PaymentApplicationService>();
        return services;
    }
}
