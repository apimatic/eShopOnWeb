using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    public static void AddPayPalEnvironmentOverrides(IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        Bind("PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Bind("PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Bind("PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Bind("PAYPAL_CURRENCY", "PayPal:Currency");
        Bind("PAYPAL_BASE_URL", "PayPal:BaseUrl");
        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }

        void Bind(string envName, string key)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                map[key] = value;
            }
        }
    }

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();

        services.AddTransient<PayPalWriteOnceHandler>();
        services.AddTransient<PayPalStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<PayPalWriteOnceHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var paypal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;

            if (string.IsNullOrWhiteSpace(paypal.ClientId) || string.IsNullOrWhiteSpace(paypal.ClientSecret))
            {
                throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
            }

            if (!string.IsNullOrWhiteSpace(paypal.Environment)
                && !paypal.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This PayPal SDK only supports the Sandbox environment. Set PayPal:Environment to sandbox.");
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
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        return services;
    }
}

internal sealed class PayPalPaymentSettings : IPaymentSettings
{
    private readonly PayPalOptions _options;

    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new InvalidOperationException("PayPal:Currency must be configured.");
            }

            return _options.Currency;
        }
    }
}
