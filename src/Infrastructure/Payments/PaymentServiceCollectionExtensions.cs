using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>
    /// Registers PayPal settings (with startup fail-fast), the PayPal SDK client, the gateway and
    /// the order-payment application service. Credentials are read from configuration
    /// (user-secrets / environment) and captured once — a rotated secret needs a process restart.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);

        // Fail fast: refuse to boot if any credential part is missing or blank. Messages name the
        // config key and never echo a value. ValidateOnStart runs during host start, not on first call.
        services.AddOptions<PayPalSettings>()
            .Bind(section)
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId is not configured. Set it via user-secrets or the environment.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret is not configured. Set it via user-secrets or the environment.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Environment), "PayPal:Environment is not configured. Set it via user-secrets or the environment.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency is not configured. Set it via user-secrets or the environment.")
            .ValidateOnStart();

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddPayPalServerSdkClient(options =>
        {
            // Only Sandbox is declared by this SDK version; PayPal:BaseUrl overrides the base URL
            // (and the token URL, which resolves from the same Default server) verbatim when set.
            options.Environment = ServerEnvironment.Sandbox;
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId ?? string.Empty,
                ClientSecret = settings.ClientSecret ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            // Per-attempt bound; the whole-call budget is enforced by the caller's CancellationToken.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };

            // Capture the last HTTP status so the gateway can map typed errors (which carry none).
            options.Hooks = new[]
            {
                SdkHook.OnResponse((response, _) => PayPalResponseContext.LastStatusCode = (int)response.StatusCode)
            };

            // LoggerFactory is left to be filled from the DI container by AddPayPalServerSdkClient,
            // which makes it non-null and thus disables the PAYPALSERVERSDKCLIENT_LOG env var.
            // LogRequestBody stays off (default), so card PAN/CVV are never written to logs.
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }
}
