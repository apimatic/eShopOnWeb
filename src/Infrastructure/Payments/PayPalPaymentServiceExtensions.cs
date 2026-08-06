using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalPaymentServiceExtensions
{
    private const string HttpClientName = "PayPalPayments";

    /// <summary>
    /// Binds <see cref="PayPalSettings"/> from configuration and registers the PayPal SDK client and
    /// <see cref="IPaymentGateway"/>. The client is a singleton over a dedicated, named
    /// <see cref="IHttpClientFactory"/> client with an explicit per-attempt timeout and a bounded
    /// pooled-connection lifetime (so DNS stays fresh behind the long-lived client). Credentials come
    /// only from configuration (env vars / user-secrets) — never from the repository.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds a single attempt (see dotnet-configuration-resilience); a hung provider ends
                // the call at ~15s rather than pinning a request thread for the SDK's 100s default.
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, BuildOptions(settings));
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    private static PayPalServerSdkClientOptions BuildOptions(PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables) via user-secrets " +
                "or configuration.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            // Only sandbox is supported for this app.
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            },
            // Cap a single attempt; the overall call is additionally bounded by the request cancellation
            // token threaded through the gateway.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
        };

        // Optional verbatim base-URL override; otherwise the SDK uses the sandbox default.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = settings.BaseUrl }
                }
            };
        }

        return options;
    }
}
