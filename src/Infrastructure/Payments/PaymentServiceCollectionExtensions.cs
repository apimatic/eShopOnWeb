using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal gateway and the payment/saved-card/reconciliation orchestration services.
/// Credentials and the optional verbatim base URL are bound from the <c>PayPal:</c> configuration
/// section; no values are hard-coded here.
/// </summary>
public static class PaymentServiceCollectionExtensions
{
    private const string PayPalHttpClientName = "PayPal";

    public static IServiceCollection AddPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // A named, long-lived HttpClient with a bounded per-attempt timeout and a pooled-connection
        // lifetime so the singleton SDK client below still picks up DNS changes.
        services.AddHttpClient(PayPalHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
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
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(e.g. via user-secrets or environment variables).");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // Sandbox is the only environment the SDK models; a different host is reached via BaseUrl below.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!
                }
            };

            // Optional verbatim override: when set, used unchanged for every call including the token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!.TrimEnd('/');
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
