using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Wires the PayPal integration into the PublicApi host: binds <c>PayPal:</c> settings, builds the SDK
/// client (with the optional verbatim base-URL override), and registers the gateway and the payment
/// application services. Only PublicApi calls this, so the storefront never takes the SDK dependency.
/// </summary>
public static class PayPalRegistration
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        // A long-lived, reused HttpClient (per SDK guidance) rather than one rebuilt per request.
        services.AddHttpClient("paypal");

        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<PayPalSettings>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("paypal");

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = s.ClientId,
                    ClientSecret = s.ClientSecret
                }
            };

            // When PayPal:BaseUrl is set, use it verbatim as the base address for EVERY call —
            // including the OAuth token request — instead of one derived from the environment.
            if (!string.IsNullOrWhiteSpace(s.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = s.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        // Scoped, not singleton: the gateway consumes the scoped IAppLogger. The heavy, reusable
        // PayPalServerSdkClient stays a singleton, so nothing per-request is rebuilt.
        services.AddScoped<IPayPalGateway, PayPalGateway>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
