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

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// Binds PayPal:* configuration to <see cref="PayPalSettings"/> and registers the PayPal SDK client
    /// and <see cref="IPaymentGateway"/>. Credentials are never read from anywhere but configuration
    /// (env vars / user-secrets) - nothing here hardcodes a value.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection("PayPal"));

        // A per-attempt timeout well under ASP.NET's own request timeout, so a hung PayPal call fails
        // fast instead of pinning the request thread for the SDK's 100s default.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));

        // AddPayPalServerSdkClient's own configure callback runs at registration time with no
        // IServiceProvider, so credentials from IOptions<PayPalSettings> can't reach it that way -
        // the client is registered directly instead, resolving settings from DI at first use.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured (e.g. via user-secrets) before the PayPal client can be built.");
            }

            var baseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl)
                ? settings.BaseUrl
                : string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(settings.Environment, "production", StringComparison.OrdinalIgnoreCase)
                    ? LiveBaseUrl
                    : SandboxBaseUrl;

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox, // the only member this generated SDK exposes; the
                                                          // base URL below is what actually selects sandbox vs live.
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };
            options.Server.Default.Sandbox.BaseUrl = baseUrl;

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
