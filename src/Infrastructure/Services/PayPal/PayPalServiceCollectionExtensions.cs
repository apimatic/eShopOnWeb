using System;
using System.Net.Http;
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
    /// <summary>
    /// Registers the PayPal SDK client (built from the "PayPal:" settings) and the payment gateway.
    /// Credentials come from configuration/user-secrets/environment — never hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // The SDK client is thread-safe and long-lived; register it as a singleton with a dedicated
        // long-lived HttpClient (the SDK manages OAuth token acquisition/refresh internally).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET) via user-secrets or configuration.");
            }

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            // Optional explicit base-URL override: used verbatim for EVERY call (incl. the token request).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server ??= new ServerOptions();
                options.Server.Default ??= new DefaultOptions();
                options.Server.Default.Sandbox ??= new DefaultOptions.SandboxOptions();
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(new HttpClient(), options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentConfiguration, PayPalPaymentConfiguration>();

        return services;
    }
}
