using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers PayPal payments: settings (fail-fast validated), the SDK client (a long-lived singleton
    /// over a named <see cref="System.Net.Http.HttpClient"/>), the gateway, and the payment/saved-card/
    /// reconciliation application services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // 1) Bind + validate settings; the host refuses to start when a credential is missing or blank.
        //    Every part is checked separately — a blank part is not a missing one.
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Environment), "PayPal:Environment is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency is not configured.")
            .Validate(s => IsSandboxTargetable(s),
                "PayPal:Environment is not 'sandbox'. This SDK build only targets the PayPal sandbox unless PayPal:BaseUrl is set to an explicit host.")
            .ValidateOnStart();

        services.AddSingleton<IPaymentConfiguration>(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        // 2) Named HttpClient — bounds one attempt and keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // 3) The SDK client as a singleton. Options are captured once at registration (a rotated secret
        //    takes effect on restart). LoggerFactory is set explicitly so the SDK's log environment
        //    variable cannot switch request-body logging on from outside the code, and LogRequestBody
        //    stays off (card data must never be logged).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };
            options.Logging = options.Logging with { LoggerFactory = sp.GetService<ILoggerFactory>() };

            // Optional base-URL override: used verbatim for every call, including the OAuth token request,
            // because the token URL resolves through Server.Default.Sandbox.BaseUrl too.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!.TrimEnd('/');

            return new PayPalServerSdkClient(httpClient, options);
        });

        // 4) Gateway + application services.
        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static bool IsSandboxTargetable(PayPalSettings s) =>
        string.Equals(s.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(s.BaseUrl);
}
