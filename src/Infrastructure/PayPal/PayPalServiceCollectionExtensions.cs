using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Binds PayPal configuration, registers the PayPal SDK client (OAuth2 client-credentials, sandbox,
    /// optional base-URL override) and the <see cref="IPayPalPaymentService"/> boundary.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddPayPalServerSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Sandbox;
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            // Bound the per-attempt timeout so a hung provider doesn't pin a request thread for ~100s.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };

            // When PayPal:BaseUrl is set, use it verbatim as the base for EVERY call (including the OAuth
            // token request) instead of deriving one from the environment.
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
        });

        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        return services;
    }
}
