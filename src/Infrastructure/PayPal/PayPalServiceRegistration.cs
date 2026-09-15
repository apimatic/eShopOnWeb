using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceRegistration
{
    private const string PayPalHttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal SDK client (bound from the "PayPal" configuration section) plus the payment,
    /// saved-card and reconciliation services. Credentials come only from configuration — nothing is hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // Own the HttpClient over a named client so timeout/handler rotation are scoped to this SDK, not the
        // app's shared default client (see dotnet-client-initialization).
        services.AddHttpClient(PayPalHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;

            if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PayPal:Environment '{settings.Environment}' is not supported by this SDK build; only 'sandbox' is available.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            // Optional base-URL override — used verbatim for EVERY call, including the OAuth token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions
                        {
                            BaseUrl = settings.BaseUrl
                        }
                    }
                };
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodAppService, PaymentMethodAppService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
