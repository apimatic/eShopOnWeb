using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public static class PayPalServiceCollectionExtensions
{
    private const string PayPalHttpClientName = "PayPal";

    /// <summary>
    /// Binds the <c>PayPal:</c> configuration section and registers a long-lived <see cref="PayPalServerSdkClient"/>
    /// (over a named, pooled <see cref="HttpClient"/>) plus the domain-facing <see cref="IPayPalPaymentService"/>.
    /// No credential value is hard-coded here — everything comes from configuration.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_SECTION));

        // A named client keeps this pipeline (timeout, pooled handler) off every other unnamed HttpClient
        // consumer. The pooled-connection lifetime keeps DNS fresh behind the long-lived singleton client.
        services.AddHttpClient(PayPalHttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // This SDK build exposes only the Sandbox environment. When a verbatim BaseUrl override is
                // configured it is applied below and used for every call, including the token request.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId ?? string.Empty,
                    ClientSecret = settings.ClientSecret ?? string.Empty,
                    Scope = null
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!.Trim();
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();

        return services;
    }
}
