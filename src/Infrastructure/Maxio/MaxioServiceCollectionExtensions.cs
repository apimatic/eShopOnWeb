using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed subscription billing capability.
    /// Settings bind from the "Maxio" configuration section (Maxio:ApiKey,
    /// Maxio:Subdomain, Maxio:ProductFamilyHandle, optional Maxio:BaseUrl);
    /// they are validated lazily on first use so app startup and tests that
    /// don't exercise billing are unaffected when the section is absent.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            client.BaseAddress = new Uri(settings.GetApiBaseUrl() + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
