using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public static class PayPalServiceExtensions
{
    public const string HttpClientName = "PayPal";

    /// <summary>
    /// Bind PayPal settings, register the SDK client over a dedicated, resilient HttpClient, and
    /// expose the payment boundary (<see cref="IPaymentProcessor"/>). Credentials come from
    /// configuration (user-secrets / environment) — never hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddTransient<PayPalStatusCaptureHandler>();

        // A dedicated (named) HttpClient keeps our timeout/handler off the shared default client.
        // Timeout bounds a single attempt so a hung provider ends the call rather than pinning a thread;
        // PooledConnectionLifetime keeps DNS fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal:ClientId and PayPal:ClientSecret must be configured (via user-secrets or environment).");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!
                }
            };

            // Optional base-URL override: when set, it must apply to EVERY call including the token
            // request. Setting Server.Default.Sandbox.BaseUrl (and leaving the default token strategy)
            // redirects token acquisition through the same resolution, per the contract sheet.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = settings.BaseUrl! }
                    }
                };
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentProcessor, PayPalPaymentService>();

        return services;
    }
}
