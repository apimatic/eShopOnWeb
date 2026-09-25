using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers PayPal payments: binds and fail-fast-validates the <c>PayPal:</c> settings, constructs the
    /// SDK client (OAuth2 client credentials, sandbox, optional base-URL override), and wires the gateway and
    /// orchestration services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: the host refuses to start if a required credential/setting is missing or blank — rather
        // than discovering it as a 401 on the first call. Each part is checked separately (blank != missing).
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId),
                "PayPal:ClientId is not configured. Set it (from env PAYPAL_CLIENT_ID) via .NET user-secrets before starting.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret),
                "PayPal:ClientSecret is not configured. Set it (from env PAYPAL_CLIENT_SECRET) via .NET user-secrets before starting.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency),
                "PayPal:Currency is not configured. Set it (from env PAYPAL_CURRENCY).")
            .ValidateOnStart();

        // The options object is built once at registration and captured in the singleton client, so a rotated
        // secret takes effect on process restart (acceptable — no hot-rotation requirement here).
        services.AddPayPalServerSdkClient(options =>
        {
            var settings = new PayPalOptions();
            configuration.GetSection(PayPalOptions.SectionName).Bind(settings);

            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId ?? throw new InvalidOperationException(
                    "PayPal:ClientId is not configured (env PAYPAL_CLIENT_ID)."),
                ClientSecret = settings.ClientSecret ?? throw new InvalidOperationException(
                    "PayPal:ClientSecret is not configured (env PAYPAL_CLIENT_SECRET).")
            };

            // Only environment this SDK exposes. A base-URL override, when set, is used verbatim for every
            // call — including the OAuth token request, which resolves through this same server node.
            options.Environment = ServerEnvironment.Sandbox;
            if (settings.HasBaseUrlOverride)
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!.Trim();

            // Per-attempt timeout (the whole-call budget is enforced by the gateway's CancellationToken).
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) };

            // Card data travels in request bodies — never log bodies. LoggerFactory is filled from DI by the
            // extension, which also disables the PAYPALSERVERSDKCLIENT_LOG env var from switching body logging on.
            options.Logging = new LoggingOptions
            {
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            };
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
