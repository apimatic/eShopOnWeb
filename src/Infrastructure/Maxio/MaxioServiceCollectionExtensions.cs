using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing client and the subscription billing service.
/// Validates configuration at registration so the host refuses to start when a credential or the
/// target site/catalog is missing — rather than failing as a 401/404 on the first request.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    // Basic auth password is the fixed literal "x"; the username carries the Chargify API key.
    private const string BasicAuthPassword = "x";

    // Per-attempt SDK timeout. The whole-operation budget is enforced separately in the service.
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        // Fail-fast: name the missing config key, never echo a value, never fall back to a default.
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or environment (MAXIO_API_KEY) before starting the app.");
        }
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Neither Maxio:BaseUrl nor Maxio:Subdomain is configured; one is required. Set Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl before starting the app.");
        }
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it (MAXIO_DEFAULT_PRODUCT_FAMILY) before starting the app.");
        }

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = BasicAuthPassword
            };
            options.Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout };

            // Maxio:BaseUrl, when set, is used verbatim as the API base address; otherwise the site
            // subdomain fills the https://{site}.chargify.com template.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionService>();
        return services;
    }
}
