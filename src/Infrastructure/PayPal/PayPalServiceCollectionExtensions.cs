using System;
using System.Net.Http;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// Binds PayPal settings from the "PayPal" configuration section and registers the PayPal SDK
    /// client (over a dedicated, timeout-bounded HttpClient) and the payment gateway.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        services.AddSingleton(settings);

        // Dedicated, timeout-bounded HttpClient so a hung provider cannot pin a request thread,
        // and a rotating pooled handler so a long-lived client keeps DNS fresh.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var resolved = sp.GetRequiredService<PayPalSettings>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return CreateClient(httpClient, resolved);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    /// <summary>Builds a configured PayPal SDK client. Exposed for testing over a stubbed HttpClient.</summary>
    public static PayPalServerSdkClient CreateClient(HttpClient httpClient, PayPalSettings settings)
    {
        Guard.Against.NullOrEmpty(settings.ClientId, "PayPal:ClientId");
        Guard.Against.NullOrEmpty(settings.ClientSecret, "PayPal:ClientSecret");

        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId!,
                ClientSecret = settings.ClientSecret!
            }
        };

        // One BaseUrl value governs both the OAuth token request and every API call.
        options.Server.Default.Sandbox.BaseUrl = ResolveBaseUrl(settings);

        return new PayPalServerSdkClient(httpClient, options);
    }

    private static string ResolveBaseUrl(PayPalSettings settings)
    {
        // An explicit override is used verbatim (including for the token endpoint).
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            return settings.BaseUrl!;

        var isLive = string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(settings.Environment, "production", StringComparison.OrdinalIgnoreCase);
        return isLive ? LiveBaseUrl : SandboxBaseUrl;
    }
}
