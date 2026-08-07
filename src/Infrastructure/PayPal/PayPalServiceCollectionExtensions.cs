using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Registers the PayPal integration and the payment application services. Wire this up from the API
/// host's composition root (Program.cs).
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));

        services.AddSingleton<PayPalAccessTokenProvider>();

        // Named client used only for the OAuth2 token endpoint.
        services.AddHttpClient(PayPalAccessTokenProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
        });

        // Typed client for the PayPal REST APIs.
        services.AddHttpClient<PayPalApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();

        // Payment application services (defined in ApplicationCore).
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}
