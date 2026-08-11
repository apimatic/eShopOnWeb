using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal integration: the SDK client (configured from the <c>PayPal:</c> section),
/// the payment gateway, and the order-payment orchestration service.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.SectionName);
        services.Configure<PayPalOptions>(section);

        var options = section.Get<PayPalOptions>() ?? new PayPalOptions();

        services.AddPayPalServerSdkClient(clientOptions =>
        {
            clientOptions.Environment = ServerEnvironment.Sandbox;
            clientOptions.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId ?? throw new InvalidOperationException("PayPal:ClientId is not configured."),
                ClientSecret = options.ClientSecret ?? throw new InvalidOperationException("PayPal:ClientSecret is not configured.")
            };

            // Optional override: when set, used verbatim as the base for every call including the token request.
            if (!string.IsNullOrEmpty(options.BaseUrl))
            {
                clientOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl;
            }
        });

        services.AddSingleton<IPaymentConfiguration, PaymentConfiguration>();
        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
