using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal payment integration: the bound-and-validated <see cref="PayPalOptions"/>, the PayPal
/// Server SDK client (as a singleton, configured once at registration), the gateway and the payment
/// application service.
/// </summary>
public static class PaymentServiceRegistration
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + fail-fast: the host refuses to start if any credential is missing or blank.
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read the same configuration for the SDK client. The callback runs once, at registration.
        var settings = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>() ?? new PayPalOptions();

        services.AddPayPalServerSdkClient(options =>
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            // The SDK only declares the Sandbox environment; all traffic targets sandbox.
            options.Environment = ServerEnvironment.Sandbox;

            // Optional base-URL override — used verbatim for every call, including the OAuth token request
            // (the token endpoint is resolved through the Default server: server.Default("/v1/oauth2/token")).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }

            // Sensitive-data posture: LogRequestBody stays off (default) and the DI extension assigns
            // LoggerFactory, so the PAYPALSERVERSDKCLIENT_LOG environment variable cannot switch unredacted
            // request-body logging on. Card details therefore never reach the SDK's logs.
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }
}
