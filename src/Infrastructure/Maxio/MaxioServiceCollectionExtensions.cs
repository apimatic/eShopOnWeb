using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription-billing service. Binds the
    /// <c>Maxio</c> configuration section and <b>fails fast at startup</b> if a required setting is
    /// missing or blank — rather than surfacing it as a 401 on the first call in production.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        ValidateSettings(settings);

        // Make the settings (e.g. ProductFamilyHandle) available to the service.
        services.Configure<MaxioSettings>(section);

        // The SDK's DI extension builds the options ONCE at registration and captures them in a singleton
        // client backed by IHttpClientFactory. A rotated ApiKey therefore takes effect only on process
        // restart (acceptable here). The extension also fills options.Logging.LoggerFactory from the DI
        // ILoggerFactory, which disarms the MAXIOADVANCEDBILLINGCLIENT_LOG environment variable; request-body
        // logging (LogRequestBody) stays off by default.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            // HTTP Basic: username = API key, password = literal "x" (Maxio/Chargify convention).
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
            options.Environment = ServerEnvironment.Us;

            // Base address: an explicit BaseUrl override wins verbatim; otherwise derive from the subdomain
            // (the {site} template in https://{site}.chargify.com).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            // Bound a single attempt. The SDK's per-attempt Timeout defaults to 100s; the whole-operation
            // budget is enforced separately via a CancellationToken deadline in the service. POST writes
            // (CreateCustomer/CreateSubscription) are never auto-resent (default HttpMethodsToRetry excludes POST).
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void ValidateSettings(MaxioSettings settings)
    {
        // Never echo the values — name only the missing keys.
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or environment configuration before starting the app.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or environment configuration before starting the app.");
        }

        // Either an explicit BaseUrl override or a Subdomain is required to address the API.
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain (or Maxio:BaseUrl) is not configured. Set one via user-secrets or environment configuration before starting the app.");
        }
    }
}
