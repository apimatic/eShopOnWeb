using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

public static class PayPalServiceCollectionExtensions
{
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// Registers the PayPal SDK client, the <see cref="IPaymentGateway"/> implementation over it,
    /// and the application services that drive the pay-for-an-order and saved-card flows.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.ConfigSectionName);
        services.Configure<PayPalOptions>(section);

        var options = section.Get<PayPalOptions>() ?? new PayPalOptions();
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId and PayPal:ClientSecret must be configured (e.g. via 'dotnet user-secrets set') before the payment gateway can start.");
        }

        var baseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? options.BaseUrl
            : string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? LiveBaseUrl
                : SandboxBaseUrl;

        services.AddTransient<PayPalIdempotencyKeyStrippingHandler>();
        services.AddHttpClient(Options.DefaultName)
            .AddHttpMessageHandler<PayPalIdempotencyKeyStrippingHandler>();

        services.AddPayPalServerSdkClient(clientOptions =>
        {
            clientOptions.Environment = ServerEnvironment.Sandbox;
            clientOptions.Server.Default.Sandbox.BaseUrl = baseUrl;
            clientOptions.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            };
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        services.AddScoped<IOrderPaymentService>(sp => new OrderPaymentService(
            sp.GetRequiredService<IRepository<Order>>(),
            sp.GetRequiredService<IRepository<Payment>>(),
            sp.GetRequiredService<IRepository<CatalogItem>>(),
            sp.GetRequiredService<IRepository<Buyer>>(),
            sp.GetRequiredService<IPaymentGateway>(),
            sp.GetRequiredService<IOptions<PayPalOptions>>().Value.Currency));

        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
