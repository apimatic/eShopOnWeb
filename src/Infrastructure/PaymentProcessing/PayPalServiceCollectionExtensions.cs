using System;
using System.Net.Http;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal SDK client and every application service that depends on it.
    /// Credentials/environment/currency/base-url are bound from configuration section "PayPal"
    /// (backed by user-secrets in development - never hard-coded here).
    /// </summary>
    public static void ConfigurePayPalServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<PayPalOptions>(configuration.GetSection("PayPal"));
        var payPalOptions = configuration.GetSection("PayPal").Get<PayPalOptions>() ?? new PayPalOptions();

        // AddPayPalServerSdkClient resolves the DEFAULT (unnamed) IHttpClientFactory client and
        // keeps the resulting PayPalServerSdkClient as a singleton, so the default client's
        // handler must be long-lived (PooledConnectionLifetime) and bounded (Timeout) here.
        services.AddHttpClient(Options.DefaultName, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddPayPalServerSdkClient(options =>
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = payPalOptions.ClientId,
                ClientSecret = payPalOptions.ClientSecret
            };
            options.Environment = ServerEnvironment.Sandbox;
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) };

            if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = payPalOptions.BaseUrl;
            }
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
    }
}
