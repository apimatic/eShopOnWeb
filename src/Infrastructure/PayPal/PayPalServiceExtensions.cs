using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds PayPal settings from configuration, registers a long-lived PayPal SDK client over a
    /// dedicated HttpClient, and wires the gateway plus the payment application services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();
        ValidateSettings(settings);

        services.AddSingleton(settings);
        services.AddSingleton<IPaymentConfiguration>(settings);

        // Dedicated HttpClient: a bounded per-attempt timeout, and a rotated connection pool so a
        // long-lived (singleton) client does not cache DNS forever.
        var httpBuilder = services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (configuration.GetValue<bool>($"{PayPalSettings.SectionName}:WireLog"))
            httpBuilder.AddHttpMessageHandler(() => new PayPalWireLogHandler());

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox, // the SDK exposes only Sandbox; a custom host is set below
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // When PayPal:BaseUrl is set, use it verbatim for every call, including the token call —
            // the whole SDK resolves against this single base URL.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static void ValidateSettings(PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(settings.ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        if (string.IsNullOrWhiteSpace(settings.Currency))
            throw new InvalidOperationException("PayPal:Currency is not configured.");
        if (string.IsNullOrWhiteSpace(settings.Environment))
            throw new InvalidOperationException("PayPal:Environment is not configured.");

        // The SDK has no Live environment enum; a non-sandbox target must supply a base URL to hit.
        if (!settings.IsSandbox && string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new InvalidOperationException(
                "PayPal:Environment is not 'sandbox' but PayPal:BaseUrl is not set. " +
                "Set PayPal:BaseUrl to target a non-sandbox environment.");
    }
}
