using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal integration: strongly-typed <see cref="PayPalOptions"/> (validated on
    /// start so the host refuses to boot with a missing/blank credential), the PayPal SDK client,
    /// and the payment gateway + orchestration service.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: every required credential must be present and non-blank before the app serves
        // anything. Both parts of the credential are checked (a blank part is not a missing one).
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PayPal:ClientId is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment), "PayPal:Environment is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency), "PayPal:Currency is not configured.")
            .ValidateOnStart();

        // The SDK client is a singleton; its options object is captured once, here, at registration.
        // A rotated secret therefore takes effect only on process restart (documented in the plan).
        services.AddPayPalServerSdkClient(options =>
        {
            var clientId = configuration[$"{PayPalOptions.SectionName}:ClientId"];
            var clientSecret = configuration[$"{PayPalOptions.SectionName}:ClientSecret"];
            var baseUrl = configuration[$"{PayPalOptions.SectionName}:BaseUrl"];

            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = clientId ?? string.Empty,
                ClientSecret = clientSecret ?? string.Empty
            };

            // The SDK declares only a Sandbox environment, so all traffic targets sandbox.
            options.Environment = ServerEnvironment.Sandbox;

            // Optional verbatim base-URL override. Because the OAuth token URL is resolved through
            // Server.Default (Sandbox.BaseUrl), this single override also covers the token request.
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = baseUrl!;
            }

            // Sensitive data (card PAN/CVV) travels in request bodies, so request-body logging stays
            // OFF (its default). LoggerFactory is left null here so the DI extension fills it from the
            // host's ILoggerFactory — which also disables the PAYPALSERVERSDKCLIENT_LOG environment
            // variable, so body logging cannot be forced on from outside the code.
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }
}
