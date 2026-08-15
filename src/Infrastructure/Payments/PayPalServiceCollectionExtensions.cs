using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal SDK client and the <see cref="IPayPalGateway"/> implementation.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    public static IServiceCollection AddPayPalGateway(this IServiceCollection services, PayPalOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // The SDK's sanctioned DI extension wires an IHttpClientFactory-managed HttpClient and captures the
        // options once, at registration. Credentials/base-URL are read from configuration-bound options.
        services.AddPayPalServerSdkClient(o =>
        {
            o.Environment = ServerEnvironment.Sandbox;

            o.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            };

            var baseUrl = ResolveBaseUrl(options);
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                // The Sandbox environment's BaseUrl is re-resolved on every request AND supplies the OAuth
                // token call (server.Default("/v1/oauth2/token")), so an override reaches token acquisition too.
                o.Server.Default.Sandbox.BaseUrl = baseUrl;
            }
        });

        services.AddSingleton(options);
        services.AddScoped<IPayPalGateway, PayPalGateway>();
        return services;
    }

    private static string? ResolveBaseUrl(PayPalOptions options)
    {
        // Explicit verbatim override wins for every call, including the OAuth token request.
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)) return options.BaseUrl;

        // No override + a live/production environment => point the (only) Sandbox environment at the live host.
        if (IsLive(options.Environment)) return LiveBaseUrl;

        // Otherwise leave the default sandbox host in place.
        return null;
    }

    private static bool IsLive(string? environment) =>
        environment is not null
        && (environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || environment.Equals("production", StringComparison.OrdinalIgnoreCase));
}
