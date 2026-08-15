using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// DI wiring for the PayPal gateway: binds <see cref="PayPalSettings"/>, registers the
/// <see cref="PayPalServerSdkClient"/> (OAuth2 client-credentials, sandbox environment, and the
/// verbatim base-URL override when configured), and maps <see cref="IPayPalGateway"/> to
/// <see cref="PayPalGateway"/>.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("PayPal");
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        // Registers an IHttpClientFactory-managed HttpClient and captures these options once, at
        // registration time (the SDK's own extension; see AsadAli.Checkout.Sdk).
        services.AddPayPalServerSdkClient(o =>
        {
            o.Environment = ServerEnvironment.Sandbox;

            o.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            // CRITICAL: force a verbatim base URL for EVERY call — including the OAuth token
            // request, which the SDK resolves from this same Sandbox.BaseUrl
            // ({BaseUrl}/v1/oauth2/token). Must be set on the Sandbox options because Sandbox is
            // the environment selected above and the only one the SDK resolves.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                o.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions
                        {
                            BaseUrl = settings.BaseUrl
                        }
                    }
                };
            }
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();

        return services;
    }
}
