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
    /// Binds the <c>Maxio:</c> settings, registers the Maxio Advanced Billing SDK client (auth,
    /// server/base-url, and a write-safe retry posture) over an <see cref="System.Net.Http.IHttpClientFactory"/>-managed
    /// HttpClient, and registers <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;

            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = "x"
            };

            // An explicit BaseUrl overrides the subdomain-derived template verbatim; otherwise the
            // base URL is derived from the site subdomain.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            // Retries cannot be disabled (the floor is 1), but keeping them at the floor with a
            // bounded per-attempt timeout minimises the window in which a transport failure could
            // re-send a create. The primary double-submit guard is the per-subscriber gate plus
            // read-before-write in the service.
            options.Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(20)
            };
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
