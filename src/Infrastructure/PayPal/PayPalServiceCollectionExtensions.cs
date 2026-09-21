using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Binds <c>PayPal:</c> settings, fails fast if a credential is missing, registers the PayPal
    /// Server SDK client (credentials + environment + optional base-URL override), the gateway, and
    /// the payment/saved-card/reconciliation services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new PayPalOptions();
        configuration.GetSection(PayPalOptions.SectionName).Bind(options);
        options.Validate(); // fail-fast at startup — the host refuses to boot on a missing credential.

        services.AddSingleton(options);

        services.AddPayPalServerSdkClient(sdk =>
        {
            sdk.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            };
            // Only Sandbox is a declared SDK environment; a different account/host is reached via BaseUrl.
            sdk.Environment = ServerEnvironment.Sandbox;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                // Used verbatim for every call, including the OAuth token request.
                sdk.Server.Default.Sandbox.BaseUrl = options.BaseUrl!.TrimEnd('/');
            }
            // Request bodies carry raw card data — never log them, and pin the logger so the
            // PAYPALSERVERSDKCLIENT_LOG env var cannot switch body logging on from outside the code.
            sdk.Logging = sdk.Logging with { LogRequestBody = false, LogRequestHeaders = false };
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
