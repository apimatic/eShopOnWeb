using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Registers the PayPal SDK client and the payment gateway. The client is built over a named,
/// long-lived <see cref="HttpClient"/> (so <see cref="IHttpClientFactory"/> connection rotation and
/// a bounded per-attempt timeout apply) and is configured entirely from <see cref="PayPalSettings"/>.
/// </summary>
public static class PayPalRegistration
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        services.AddTransient<PayPalWireLogHandler>();
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler<PayPalWireLogHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Money-moving POSTs must not be silently re-sent. MaxRetries has a floor of 1 (two
                // attempts) in this SDK; the transport-retry double-submit is closed off separately by
                // per-write idempotency keys and local state guards. Bound each attempt at 30s.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            // Optional explicit base URL: when set, used verbatim for every call including the token
            // request (the OAuth endpoint resolves through this same Server.Default node).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
