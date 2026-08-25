using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

public static class PayPalDependencies
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPaymentProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.SectionName);
        services.Configure<PayPalOptions>(section);

        var payPalOptions = section.Get<PayPalOptions>() ?? new PayPalOptions();
        if (string.IsNullOrWhiteSpace(payPalOptions.ClientId) || string.IsNullOrWhiteSpace(payPalOptions.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId and PayPal:ClientSecret must be configured (via user-secrets locally, or the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }

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
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Default(),
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = payPalOptions.ClientId,
                    ClientSecret = payPalOptions.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) }
            };

            // PayPal:BaseUrl override — applies verbatim to every call, including the OAuth token
            // request, because the token strategy resolves its URL through this same Server instance.
            if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
            {
                clientOptions.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = payPalOptions.BaseUrl }
                    }
                };
            }

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentProvider, PayPalPaymentProvider>();

        return services;
    }
}
