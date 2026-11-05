using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed subscription service from the "Maxio" configuration section.
    /// Missing configuration does not stop the host: subscription calls fail with a clear
    /// <see cref="BillingUnavailableException"/> so unrelated endpoints keep working.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));
        services.AddMemoryCache();
        services.AddHttpClient();

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Subdomain)
                || string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
            {
                throw new BillingUnavailableException(
                    "Subscription billing is not configured (Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle are required).");
            }

            var options = new MaxioAdvancedBillingClientOptions
            {
                // The API key is the basic-auth username; the password is an unused placeholder.
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Environment = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                    ? ServerEnvironment.Eu
                    : ServerEnvironment.Us,
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                var baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
                options.Server.Production.Us.BaseUrl = baseUrl;
                options.Server.Production.Eu.BaseUrl = baseUrl;
            }

            return new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), options);
        });
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
