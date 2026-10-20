using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio client (singleton) and the subscription service. Fails fast at startup
    /// when the Maxio settings are missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();

        services.AddSingleton(Options.Create(settings));

        // Options are built once, when the singleton is first resolved: a rotated secret needs a restart.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
            options.Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us;

            if (settings.IsEu)
            {
                options.Server.Production.Eu.Site = settings.Subdomain!;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            // Per-attempt timeout; the whole-call budget is enforced by the service (Maxio:RequestBudgetSeconds).
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) };
            // Request bodies carry customer personal data: never log them.
            options.Logging = options.Logging with { LogRequestBody = false };
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
