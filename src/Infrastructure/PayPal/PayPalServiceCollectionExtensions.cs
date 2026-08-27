using System;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Binds the PayPal: configuration section (ClientId, ClientSecret, Environment,
    /// Currency, optional BaseUrl) and registers the hand-written PayPal client plus the
    /// payment orchestration service.
    /// </summary>
    public static IServiceCollection AddPayPal(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);

        var settings = new PayPalSettings
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? "sandbox",
            Currency = section["Currency"] ?? "USD",
            BaseUrl = section["BaseUrl"]
        };
        services.AddSingleton(settings);

        services.AddHttpClient<IPayPalClient, PayPalClient>(client =>
        {
            client.BaseAddress = new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/");
        });

        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
