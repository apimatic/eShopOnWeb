using System;
using System.Net.Http;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
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
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal SDK client and the eShop payment integration. Settings are bound from the
    /// <c>PayPal:</c> configuration section; no value is hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_NAME));

        // A dedicated, long-lived HttpClient pipeline for the SDK: an explicit per-attempt timeout (so a hung
        // provider can't pin a request thread) and a pooled-connection lifetime (the SDK client is a
        // singleton, so IHttpClientFactory handler rotation would otherwise never reach it). The status
        // handler lets the error boundary recover the HTTP status even if a JsonException hides it.
        services.AddTransient<StatusCapturingHandler>();
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(40))
            .AddHttpMessageHandler<StatusCapturingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            Guard.Against.NullOrEmpty(settings.ClientId, "PayPal:ClientId");
            Guard.Against.NullOrEmpty(settings.ClientSecret, "PayPal:ClientSecret");

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK ships only a Sandbox environment; production is reached solely via PayPal:BaseUrl.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!
                },
                // Bound per attempt; a total-call budget is enforced via cancellation at the call boundary.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
            };

            // Verbatim base-URL override — this single property also redirects the OAuth2 token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway>(sp =>
        {
            var client = sp.GetRequiredService<PayPalServerSdkClient>();
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var currency = Guard.Against.NullOrEmpty(settings.Currency, "PayPal:Currency");
            var logger = sp.GetRequiredService<IAppLogger<PayPalGateway>>();
            return new PayPalGateway(client, currency, logger);
        });

        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
