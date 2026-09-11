using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Registers everything needed to drive PayPal payments and saved cards: settings binding, the
/// OAuth token pipeline, the typed gateway HttpClient, and the payment application services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<IPaymentConfiguration, PayPalPaymentConfiguration>();
        services.AddSingleton<PayPalTokenProvider>();
        services.AddTransient<PayPalAuthHandler>();

        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();
        var baseAddress = EnsureTrailingSlash(settings.ResolveBaseUrl());

        // Client used only for the OAuth token request (HTTP Basic, no bearer handler).
        services.AddHttpClient(PayPalHttpClientNames.Auth, c => c.BaseAddress = baseAddress);

        // Typed gateway client: base URL + bearer-token handler.
        services.AddHttpClient<IPaymentGateway, PayPalGateway>(PayPalHttpClientNames.Api, c => c.BaseAddress = baseAddress)
            .AddHttpMessageHandler<PayPalAuthHandler>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var text = uri.AbsoluteUri;
        return text.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(text + "/", UriKind.Absolute);
    }
}
