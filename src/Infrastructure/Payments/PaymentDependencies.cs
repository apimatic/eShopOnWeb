using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentDependencies
{
    /// <summary>
    /// Registers PayPal configuration (with startup fail-fast), the PayPal SDK client, the gateway, and the
    /// payment orchestration service.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind PayPal:* and refuse to start if a credential is missing or blank. Each part is checked
        // individually (a blank part is not a missing one).
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PayPal:ClientId is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment), "PayPal:Environment is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency), "PayPal:Currency is not configured.")
            .Validate(o => o.IsSandbox || !string.IsNullOrWhiteSpace(o.BaseUrl),
                "PayPal:BaseUrl must be set when PayPal:Environment is not 'sandbox' (this SDK only ships a Sandbox environment).")
            .ValidateOnStart();

        // Read the values once, at registration, for the SDK client's captured options.
        var payPal = new PayPalOptions();
        configuration.GetSection(PayPalOptions.SectionName).Bind(payPal);

        services.AddPayPalServerSdkClient(options =>
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = payPal.ClientId,
                ClientSecret = payPal.ClientSecret
            };

            // The SDK only declares a Sandbox environment; a different PayPal host is expressed via BaseUrl,
            // which the token request also resolves through (Server.Default.Sandbox.BaseUrl).
            options.Environment = ServerEnvironment.Sandbox;
            if (!string.IsNullOrWhiteSpace(payPal.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = payPal.BaseUrl!;
            }

            // Card data is sensitive: assign the logger factory explicitly (so the SDK's log env-var cannot
            // switch body logging on) and keep request-body logging off.
            options.Logging = options.Logging with
            {
                LoggerFactory = NullLoggerFactory.Instance,
                LogRequestBody = false
            };

            // Per-attempt timeout (the whole-call budget is a CancellationToken deadline at the API boundary).
            options.Retry = RetryOptions.Default() with { Timeout = System.TimeSpan.FromSeconds(30) };
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
