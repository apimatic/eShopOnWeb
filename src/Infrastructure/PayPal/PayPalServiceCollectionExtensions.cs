using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires up the PayPal payment integration: options binding, HTTP clients, the token provider,
/// the <see cref="IPaymentGateway"/> implementation and the payment application services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));

        // Dedicated client for the OAuth2 token endpoint.
        services.AddHttpClient(PayPalAccessTokenProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<PayPalAccessTokenProvider>();

        // Typed client for the PayPal REST APIs.
        services.AddHttpClient<IPaymentGateway, PayPalClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }
}
