using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires up the PayPal integration: binds the <c>PayPal:</c> settings, constructs a long-lived SDK client
/// (honouring the base-URL override and environment), and registers the gateway and payment services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();
        services.AddSingleton(settings);

        // A long-lived, factory-managed HttpClient. The per-attempt timeout bounds a hung provider; the
        // pooled-connection lifetime keeps DNS fresh behind the singleton client below.
        services.AddTransient<StatusCapturingHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<StatusCapturingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = BuildOptions(settings);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }

    private static PayPalServerSdkClientOptions BuildOptions(PayPalSettings settings)
    {
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId ?? string.Empty,
                ClientSecret = settings.ClientSecret ?? string.Empty
            },
            // Bound one attempt; total-call budget is the request's CancellationToken at the endpoint.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
        };

        // Base-URL selection. The SDK exposes only a Sandbox environment, so production and any explicit
        // override are both applied by overriding the (literally named) Sandbox base URL — which the resolver
        // uses for every call, including the OAuth/token request.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        }
        else if (settings.IsProduction)
        {
            options.Server.Default.Sandbox.BaseUrl = LiveBaseUrl;
        }

        return options;
    }
}
