using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal payment gateway, binding <see cref="PayPalSettings"/> from the
    /// <c>PayPal:</c> configuration section (supplied via configuration / user-secrets).
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        var settings = new PayPalSettings
        {
            ClientId = section["ClientId"],
            ClientSecret = section["ClientSecret"],
            Environment = section["Environment"],
            BaseUrl = section["BaseUrl"]
        };
        // Validation is deferred to first use (see PayPalAccessTokenProvider / gateway) so hosts that
        // never exercise payments — e.g. the test environment — can still start without PayPal config.

        services.AddSingleton(settings);
        services.AddSingleton<PayPalAccessTokenProvider>();

        // Named client used only by the token endpoint.
        services.AddHttpClient(PayPalAccessTokenProvider.HttpClientName);

        // Typed client for the gateway itself.
        services.AddHttpClient<IPayPalPaymentGateway, PayPalPaymentGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        return services;
    }
}
