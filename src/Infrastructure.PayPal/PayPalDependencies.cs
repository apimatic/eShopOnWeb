using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalDependencies
{
    public const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds and validates the PayPal: configuration section, registers the SDK client as a
    /// singleton over a named, factory-managed HttpClient, and registers the payment services.
    /// Fails fast at startup when the configuration is missing or unsupported.
    /// </summary>
    public static IServiceCollection AddPayPalServices(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>() ?? new PayPalOptions();
        Validate(options);
        services.AddSingleton(options);

        services.AddTransient<PayPalLoggingHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler<PayPalLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton, so the pooled handler must rotate to keep DNS fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret
                },
                // Per-attempt bound; the gateway adds a total call budget via cancellation.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                // Verbatim override for every PayPal call, including the OAuth token request.
                clientOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl;
            }
            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<ICardVault, PayPalCardVault>();
        services.AddScoped<ITransactionSearch, PayPalTransactionSearch>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddScoped<IOrderQueryService, OrderQueryService>();

        return services;
    }

    private static void Validate(PayPalOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId is not configured. Provide it via user-secrets or environment-specific configuration; never commit it.");
        }
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientSecret is not configured. Provide it via user-secrets or environment-specific configuration; never commit it.");
        }
        if (string.IsNullOrWhiteSpace(options.Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured.");
        }
        if (!string.Equals(options.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PayPal:Environment '{options.Environment}' is not supported: the PayPal SDK in use exposes only the Sandbox environment. " +
                "Set PayPal:Environment to 'sandbox'; use PayPal:BaseUrl to override the API host.");
        }
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !Uri.IsWellFormedUriString(options.BaseUrl, UriKind.Absolute))
        {
            throw new InvalidOperationException("PayPal:BaseUrl must be an absolute URL when set.");
        }
    }
}
