using System;
using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioClientRegistration
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);

        // The client is built lazily (on first use) so the app can start without billing
        // configured; billing endpoints then answer 503 with a reason.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();
            Configure(options, settings);
        });
        services.AddScoped<IBillingService, BillingService>();
        return services;
    }

    internal static void Configure(MaxioAdvancedBillingClientOptions options, MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: Maxio:ApiKey is missing.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: Maxio:Subdomain is missing.");

        var environment = ServerEnvironment.Default();
        if (!string.IsNullOrWhiteSpace(settings.Environment))
        {
            var value = settings.Environment.Trim();
            if (string.Equals(value, "US", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Us;
            else if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Eu;
            else throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: Maxio:Environment must be US or EU.");
        }

        options.Environment = environment;
        options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
        options.Server.Production.Us.Site = settings.Subdomain!;
        options.Server.Production.Eu.Site = settings.Subdomain!;

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim as the API base address instead of the one derived from the subdomain.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
    }
}
