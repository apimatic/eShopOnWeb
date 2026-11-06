using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioClientFactory
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services)
    {
        services.AddOptions<MaxioSettings>().BindConfiguration(MaxioSettings.SectionName);
        services.AddHttpClient();
        // Built lazily on first use, so the app still starts (and non-billing endpoints still work)
        // when the Maxio settings are absent.
        services.AddSingleton(sp => Create(
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            sp.GetService<ILoggerFactory>()));
        services.AddScoped<IBillingService, BillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClient Create(MaxioSettings settings, HttpClient httpClient, ILoggerFactory? loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) ||
            string.IsNullOrWhiteSpace(settings.Subdomain) ||
            string.IsNullOrWhiteSpace(settings.Environment))
        {
            throw new BillingException(503,
                "Billing is not configured: Maxio:ApiKey, Maxio:Subdomain and Maxio:Environment must be set.");
        }

        ServerEnvironment environment;
        if (string.Equals(settings.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase))
            environment = ServerEnvironment.Us;
        else if (string.Equals(settings.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
            environment = ServerEnvironment.Eu;
        else
            throw new BillingException(503, "Billing is not configured: Maxio:Environment must be 'US' or 'EU'.");

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            // The API key is the basic-auth user name; Maxio ignores the password.
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
        };
        options.Server.Production.Us.Site = settings.Subdomain!;
        options.Server.Production.Eu.Site = settings.Subdomain!;
        if (loggerFactory != null)
        {
            options.Logging = options.Logging with { LoggerFactory = loggerFactory };
        }
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
