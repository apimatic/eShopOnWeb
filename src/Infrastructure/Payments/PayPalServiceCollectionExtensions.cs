using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    public const string AuthHttpClientName = "PayPal.Auth";

    /// <summary>
    /// Registers the PayPal payment processor (hand-written client built on the OpenAPI
    /// documents in api-specs/paypal). Settings bind from the PayPal: section.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<PayPalTokenProvider>();
        services.AddTransient<PayPalAuthHandler>();

        services.AddHttpClient(AuthHttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<PayPalClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<PayPalAuthHandler>();

        services.AddTransient<IPaymentProcessor, PayPalPaymentProcessor>();
        return services;
    }
}
