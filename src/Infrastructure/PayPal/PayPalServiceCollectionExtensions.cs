using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal integration: binds <see cref="PayPalOptions"/> from the "PayPal" configuration section,
/// registers a long-lived PayPal SDK client with OAuth2 client-credentials, and registers the payment,
/// checkout, saved-card and reconciliation services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.CONFIG_SECTION);
        services.Configure<PayPalOptions>(section);
        var options = section.Get<PayPalOptions>() ?? new PayPalOptions();

        services.AddPayPalServerSdkClient(o =>
        {
            o.Environment = ServerEnvironment.Sandbox;

            // The SDK performs the OAuth2 client-credentials token exchange itself and attaches the bearer.
            o.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId ?? string.Empty,
                ClientSecret = options.ClientSecret ?? string.Empty,
                Scope = null
            };

            // Bound so a hung provider cannot pin a request thread indefinitely (per-attempt).
            o.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };

            // Base-URL override: when set, use it verbatim for every call including the token request.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                o.Server.Default.Sandbox.BaseUrl = options.BaseUrl!;
            }
            else if (IsLive(options.Environment))
            {
                // No Live environment member exists in this SDK release; the base-URL override is the path to Live.
                o.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com";
            }
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderCheckoutService, OrderCheckoutService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static bool IsLive(string? environment) =>
        environment is not null
        && (environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || environment.Equals("production", StringComparison.OrdinalIgnoreCase));
}
