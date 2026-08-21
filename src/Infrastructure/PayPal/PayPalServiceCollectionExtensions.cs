using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal SDK client and the payment/saved-card/reconciliation services into DI. All
/// settings are bound from the "PayPal" configuration section; no value is hard-coded.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);
        services.Configure<PayPalSettings>(section);
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        // Credentials/currency are not validated here so the host still boots in environments that do not
        // exercise payments (e.g. the test host). Missing credentials surface as a clear PayPal auth error
        // on the first real call rather than a startup crash.

        // A named HttpClient keeps this SDK's timeout/handler off the shared default client.
        // Timeout bounds a single attempt; PooledConnectionLifetime keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler(() => new PayPalStatusCaptureHandler())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, BuildOptions(settings));
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

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
            }
        };

        var env = settings.Environment?.Trim();
        var isSandbox = string.IsNullOrEmpty(env) || env.Equals("Sandbox", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // When set, the override is the base address for EVERY call, including the OAuth token request.
            options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        }
        else if (!isSandbox)
        {
            throw new InvalidOperationException(
                $"PayPal:Environment '{env}' is not supported by this SDK build (only 'Sandbox'). " +
                "Set PayPal:BaseUrl to target another host.");
        }

        return options;
    }
}
