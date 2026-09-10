using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription-billing service.
    /// Validates credentials at registration and throws so the host refuses to start when the
    /// <c>Maxio</c> configuration is incomplete — rather than surfacing it as a 401 on the first call.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        ValidateOrThrow(settings);

        services.Configure<MaxioSettings>(section);

        // AddMaxioAdvancedBillingClient wires an IHttpClientFactory-managed HttpClient and captures
        // these options ONCE, in the singleton client — so a rotated API key takes effect on restart.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            // Basic auth: username = Chargify API key, password = the literal "x".
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x",
            };

            // Sandbox and production both run on the US environment (Basic auth is US/EU only).
            options.Environment = ServerEnvironment.Us;

            // BaseUrl override wins verbatim; otherwise the subdomain fills the {site} template.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            // Per-attempt timeout kept short; POST writes are not resent by the SDK (verb list),
            // and each endpoint additionally passes the request-abort token to bound the whole call.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private static void ValidateOrThrow(MaxioSettings settings)
    {
        // Never echo the values — only the missing key names.
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or an environment variable before starting.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or an environment variable before starting.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set). Set it via user-secrets or an environment variable before starting.");
        }
    }
}
