using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal SDK client and the payment/reconciliation services from the "PayPal"
/// configuration section. No PayPal value is hard-coded — the credentials come from configuration
/// (user-secrets / environment), so the same build runs against a different account unchanged.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);
        services.Configure<PayPalSettings>(section);
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddPayPalServerSdkClient(options =>
        {
            // The SDK exposes only the Sandbox environment; production is targeted via PayPal:BaseUrl.
            options.Environment = ServerEnvironment.Sandbox;

            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            // When PayPal:BaseUrl is set, use it verbatim for every call — including the OAuth token
            // request, which the SDK builds from this same Sandbox base URL.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            // Bound each attempt; the total is bounded by the caller's CancellationToken.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };
        });

        // The SDK registers its client as a singleton over the default IHttpClientFactory client;
        // keep DNS fresh behind that long-lived client.
        var wireLog = string.Equals(section["WireLog"], "true", StringComparison.OrdinalIgnoreCase);
        var httpClientBuilder = services.AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (wireLog)
        {
            services.AddTransient<PayPalWireLoggingHandler>();
            httpClientBuilder.AddHttpMessageHandler<PayPalWireLoggingHandler>();
        }

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
