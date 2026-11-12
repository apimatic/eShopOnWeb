using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" section, fails startup when it is incomplete, and registers the (singleton) SDK client.
    /// Options are captured once at registration: a rotated key takes effect on restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim as the API base address.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            }
            // Per-attempt timeout; the whole call is bounded by the service's 30 s budget.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) };
        });

        services.AddScoped<MaxioSubscriptionService>();
        return services;
    }
}
