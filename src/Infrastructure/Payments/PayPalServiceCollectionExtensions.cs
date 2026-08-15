using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds <see cref="PayPalSettings"/> from the <c>PayPal:</c> configuration section and registers
    /// the PayPal SDK client (over a named, pooled <see cref="HttpClient"/>) plus the
    /// <see cref="IPayPalPaymentGateway"/> adapter.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // Also register the bound settings as a concrete singleton so ApplicationCore services can
        // depend on PayPalSettings directly (e.g. for the configured currency) without IOptions.
        services.AddSingleton(ReadSettings(configuration));

        // Named HttpClient: keep this pipeline off the shared default client, bound the per-attempt
        // timeout, and keep DNS fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = ReadSettings(configuration);
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // This SDK exposes only the Sandbox environment. A non-sandbox account is reached by
                // setting PayPal:BaseUrl (the verbatim override below), which the SDK also uses for the
                // OAuth token request.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPayPalPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    private static PayPalSettings ReadSettings(IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        return settings;
    }
}
