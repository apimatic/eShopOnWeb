using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>PayPal:</c> configuration section and wires the payment gateway onto a pooled,
    /// time-bounded HTTP client. The application starts whether or not payments are configured - the
    /// catalog and the basket are still there - but a payment attempted without configuration is
    /// refused with a message that says which setting is missing.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(option => Bind(option, configuration));

        services.AddSingleton<PayPalAccessTokenProvider>();

        services.AddHttpClient(PayPalAccessTokenProvider.HTTP_CLIENT_NAME, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient(PayPalPaymentGateway.HTTP_CLIENT_NAME, client =>
        {
            // A hold, a capture or a refund that times out has to be retried under the same request id,
            // so the timeout is generous enough for the processor to answer rather than hang the caller.
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        // The gateway asks the factory for its own client per call, so handlers are pooled and rotated
        // while the single-instance token cache stays warm.
        services.AddTransient<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    /// <summary>
    /// What the <c>PayPal:</c> section is missing, or null when it is complete. Logged at startup so a
    /// misconfigured deployment is visible before a shopper finds out the hard way.
    /// </summary>
    public static string? PayPalConfigurationProblem(this IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        Bind(settings, configuration);
        return settings.Problem;
    }

    /// <summary>
    /// Reads the <c>PayPal:</c> keys, falling back to the documented environment variables for any that
    /// configuration does not carry. Values are never hard-coded: whichever source a deployment uses,
    /// the same build runs against a different PayPal account.
    /// </summary>
    private static void Bind(PayPalSettings settings, IConfiguration configuration)
    {
        settings.ClientId = Value(configuration, PayPalSettings.CLIENT_ID_KEY, "PAYPAL_CLIENT_ID");
        settings.ClientSecret = Value(configuration, PayPalSettings.CLIENT_SECRET_KEY, "PAYPAL_CLIENT_SECRET");
        settings.Environment = Value(configuration, PayPalSettings.ENVIRONMENT_KEY, "PAYPAL_ENVIRONMENT");
        settings.Currency = Value(configuration, PayPalSettings.CURRENCY_KEY, "PAYPAL_CURRENCY");
        settings.BaseUrl = Value(configuration, PayPalSettings.BASE_URL_KEY, "PAYPAL_BASE_URL");
    }

    private static string Value(IConfiguration configuration, string key, string environmentVariable)
    {
        var configured = configuration[$"{PayPalSettings.SECTION_NAME}:{key}"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return (Environment.GetEnvironmentVariable(environmentVariable) ?? string.Empty).Trim();
    }
}
