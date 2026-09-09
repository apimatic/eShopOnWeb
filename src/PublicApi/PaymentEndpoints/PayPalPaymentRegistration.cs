using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Registers the PayPal payment capability: settings bound from <c>PayPal:</c> with fail-fast
/// validation, the PayPal SDK client, and the gateway + application service.
/// </summary>
public static class PayPalPaymentRegistration
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + fail-fast validate the PayPal settings so the host refuses to start when a
        // credential is missing/blank, rather than surfacing later as a 401 on the first call.
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(s => string.Equals(s.Environment, "sandbox", StringComparison.OrdinalIgnoreCase),
                "PayPal:Environment must be 'sandbox' for this build.")
            .ValidateOnStart();

        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();

        // Register the SDK client (singleton, over an IHttpClientFactory-managed HttpClient). The
        // extension fills LoggerFactory from DI, which also disables the PAYPALSERVERSDKCLIENT_LOG
        // environment variable — so card data can never be forced into logs from outside the code.
        // LogRequestBody stays off (the default), so JSON bodies carrying card data are not logged.
        services.AddPayPalServerSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Sandbox;
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            // Optional base-URL override — when set, used verbatim for EVERY call, including the
            // OAuth token request (the token URL resolves through this same Sandbox base URL).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
