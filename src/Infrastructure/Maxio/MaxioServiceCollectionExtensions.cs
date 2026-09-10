using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings (from the <c>Maxio:</c>
/// configuration section), and a typed <see cref="System.Net.Http.HttpClient"/> pre-configured
/// with the site base address and Basic-auth credentials.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.ConfigurationSection);
        var settings = new MaxioSettings
        {
            ApiKey = section["ApiKey"],
            Subdomain = section["Subdomain"],
            ProductFamilyHandle = section["ProductFamilyHandle"],
            BaseUrl = section["BaseUrl"]
        };

        // Note: settings are validated lazily on first use (see MaxioBillingService) rather
        // than here, so that an unconfigured environment (e.g. the functional test host) can
        // still boot the API; only the subscription endpoints require Maxio to be configured.
        services.AddSingleton(settings);

        // Basic auth: username = API key, password = literal "x".
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));

        services.AddHttpClient<ISubscriptionBillingService, MaxioBillingService>(client =>
        {
            client.BaseAddress = settings.ResolveBaseUri();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration");
        });

        return services;
    }
}
