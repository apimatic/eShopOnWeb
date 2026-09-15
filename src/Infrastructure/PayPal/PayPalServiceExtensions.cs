using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Composition root for the PayPal integration: binds the <c>PayPal:</c> settings, registers the
/// PayPal SDK client (with credentials, environment and optional base-URL override), the PayPal
/// service boundary, and the application services that orchestrate the money flows.
/// </summary>
public static class PayPalServiceExtensions
{
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.CONFIG_NAME).Bind(settings);
        services.AddSingleton(settings);

        services.AddPayPalServerSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Sandbox;

            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
                Scope = null
            };

            // Optional base-URL override: when set, use it verbatim for every PayPal call — the token
            // request resolves through this same base too.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = settings.BaseUrl }
                    }
                };
            }

            // Bound a single attempt so a hung provider gives way well before the 100s default.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };
        });

        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
