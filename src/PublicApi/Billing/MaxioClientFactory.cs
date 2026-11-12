using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioClientFactory
{
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        // Built once, on first use: a missing setting only fails billing requests (503), not the whole host.
        // A rotated key takes effect on restart.
        services.AddSingleton(_ =>
            Create(configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings()));
        services.AddScoped<IBillingService, BillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClient Create(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.Subdomain)
            || string.IsNullOrWhiteSpace(settings.Environment))
        {
            throw new BillingException(503,
                "Billing is not configured: Maxio:ApiKey, Maxio:Subdomain and Maxio:Environment are required.");
        }

        var environment = settings.Environment.Trim().ToUpperInvariant() switch
        {
            "US" => ServerEnvironment.Us,
            "EU" => ServerEnvironment.Eu,
            _ => throw new BillingException(503, "Billing is not configured: Maxio:Environment must be US or EU.")
        };

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Environment = environment,
            // Only reads are resent by the SDK; a lost response on a write is never replayed.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(20),
                HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head }
            }
        };

        options.Server.Production.Us.Site = settings.Subdomain;
        options.Server.Production.Eu.Site = settings.Subdomain;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, options);
    }
}
