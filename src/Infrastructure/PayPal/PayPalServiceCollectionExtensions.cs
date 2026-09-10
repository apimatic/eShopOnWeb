using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Wires up PayPal: registers the PayPal SDK client (credentials captured once at registration), the
    /// gateway, and the payment services. The caller is responsible for binding and fail-fast-validating
    /// <see cref="PayPalSettings"/> (see <c>AddPayPalSettings</c> / the host startup) — this method reads
    /// the already-available configuration to build the SDK client options.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.CONFIG_NAME).Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddPayPalServerSdkClient(options =>
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
            };
            options.Environment = ServerEnvironment.Sandbox;
            // Per-attempt timeout; the whole-call bound is a CancellationToken deadline at the call sites.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };
            // Optional verbatim base-URL override — covers every call including the OAuth token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;
            }
            // LogRequestBody stays off (default) so card numbers are never logged; LoggerFactory is filled
            // from DI by the extension, which also disables the PAYPALSERVERSDKCLIENT_LOG env override.
        });

        services.AddSingleton<IPayPalCurrencyProvider, PayPalCurrencyProvider>();
        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private sealed class PayPalCurrencyProvider : IPayPalCurrencyProvider
    {
        public PayPalCurrencyProvider(IOptions<PayPalSettings> options) => Currency = options.Value.Currency;

        public string Currency { get; }
    }
}
