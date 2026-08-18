using System;
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
    private const string PayPalHttpClientName = "PayPal";

    /// <summary>
    /// Binds the <c>PayPal:</c> settings, registers the PayPal SDK client (one long-lived instance over
    /// a dedicated, factory-managed HttpClient) and the payment services. No PayPal values are hard-coded
    /// here — they come from configuration/user-secrets.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.AddSingleton<IPaymentConfiguration>(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        // A dedicated HttpClient, kept off the shared default client. Timeout bounds one attempt; a
        // 5-minute pooled-connection lifetime keeps DNS fresh behind the long-lived (singleton) client.
        services.AddTransient<PayPalWireLoggingHandler>();
        services.AddHttpClient(PayPalHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler<PayPalWireLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Writes here (authorize/capture/refund) are non-idempotent; keep transport re-sends to the
                // floor and rely on PayPal-Request-Id for idempotency. Bound each attempt below the outer budget.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            // Optional base-URL override: when set, used verbatim for EVERY call, including the OAuth token
            // request (the token endpoint resolves through this same server path).
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

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}
