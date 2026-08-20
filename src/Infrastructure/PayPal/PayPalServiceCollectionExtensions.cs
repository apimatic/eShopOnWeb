using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddSingleton<IPayPalSettings, PayPalSettings>();
        services.AddTransient<PayPalStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);
            var paypal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            if (string.IsNullOrWhiteSpace(paypal.ClientId) || string.IsNullOrWhiteSpace(paypal.ClientSecret))
                throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
            if (!string.Equals(paypal.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(paypal.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This PayPal SDK only supports ServerEnvironment.Sandbox. Set PayPal:Environment to Sandbox.");
            }

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = paypal.ClientId,
                    ClientSecret = paypal.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(paypal.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = paypal.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<ICheckoutPaymentService, ApplicationCore.Services.CheckoutPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, ApplicationCore.Services.SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ApplicationCore.Services.ReconciliationService>();

        return services;
    }

    public static void AddPayPalEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var overrides = new System.Collections.Generic.Dictionary<string, string?>();
        Copy("PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Copy("PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Copy("PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Copy("PAYPAL_CURRENCY", "PayPal:Currency");
        Copy("PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (overrides.Count > 0)
            builder.AddInMemoryCollection(overrides);

        void Copy(string envName, string configKey)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrEmpty(value))
                overrides[configKey] = value;
        }
    }
}
