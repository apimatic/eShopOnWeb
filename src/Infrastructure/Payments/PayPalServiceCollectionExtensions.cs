using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;   // RetryOptions
using PayPalServerSdk.Servers;               // ServerEnvironment

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal SDK client and the gateway that wraps it. The SDK's own
/// <c>AddPayPalServerSdkClient</c> registers the client as a singleton over the default
/// <see cref="IHttpClientFactory"/> client, so we set a pooled-connection lifetime on that default
/// client to keep DNS fresh behind the long-lived client.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, PayPalSettings settings)
    {
        // The SDK's DI helper resolves the DEFAULT (unnamed) factory client and holds it for the process
        // lifetime; give that client a bounded per-attempt timeout and rotate its handler periodically.
        services.AddHttpClient(Options.DefaultName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddPayPalServerSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Sandbox;

            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
            };

            // Per-attempt timeout as a production-grade backstop; the SDK default (100s) is too long.
            options.Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            // Optional verbatim base-URL override. It is resolved through the same server machinery as
            // every call INCLUDING the OAuth token request, so setting it here applies everywhere.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        return services;
    }
}
