using System;
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

public static class PayPalDependencies
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PayPal:ClientId is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PayPal:ClientSecret is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency), "PayPal:Currency is required.")
            .ValidateOnStart();

        // A named client keeps this pipeline (timeout, handler lifetime) off the shared default.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var payPalOptions = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;

            var clientOptions = new PayPalServerSdkClientOptions
            {
                // The SDK models only the Sandbox environment; "live" is reached via base URL.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = payPalOptions.ClientId!,
                    ClientSecret = payPalOptions.ClientSecret!
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
            };

            // BaseUrl, when set, is used verbatim for every call including the token request.
            var baseUrl = !string.IsNullOrWhiteSpace(payPalOptions.BaseUrl)
                ? payPalOptions.BaseUrl
                : string.Equals(payPalOptions.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "https://api-m.paypal.com";
            if (baseUrl is not null)
            {
                clientOptions.Server.Default.Sandbox.BaseUrl = baseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
