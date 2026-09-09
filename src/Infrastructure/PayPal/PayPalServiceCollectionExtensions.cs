using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers PayPal payment services: bound + fail-fast-validated options, the PayPalServerSdk client
    /// (with the optional BaseUrl override applied to every call including the token request), the payment
    /// gateway, and the order-payment / saved-card application services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.SectionName);

        services.AddOptions<PayPalOptions>()
            .Bind(section)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>();

        // Values are read from configuration (user-secrets / environment) — never hard-coded.
        var options = section.Get<PayPalOptions>() ?? new PayPalOptions();

        services.AddPayPalServerSdkClient(sdk =>
        {
            sdk.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId ?? string.Empty,
                ClientSecret = options.ClientSecret ?? string.Empty
            };
            sdk.Environment = ServerEnvironment.Sandbox;

            // When PayPal:BaseUrl is set, use it verbatim for every call — the OAuth token request resolves
            // through this same Default.Sandbox.BaseUrl (see AuthSchemes.cs / DefaultOptions.cs).
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                sdk.Server.Default.Sandbox.BaseUrl = options.BaseUrl!;
            }
            // LoggerFactory is filled from DI by AddPayPalServerSdkClient (disarming the *_LOG env var),
            // and LogRequestBody defaults to false, so card data in request bodies is never logged.
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();

        return services;
    }
}
