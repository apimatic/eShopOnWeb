using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration. Settings are bound from the
    /// "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain,
    /// Maxio:ProductFamilyHandle, and the optional Maxio:BaseUrl override).
    /// Validation is deferred to first use so that a missing billing configuration
    /// never prevents the rest of the API (catalog, basket, orders) from starting.
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.SectionName).Bind(settings);

        services.AddSingleton(settings);

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            settings.Validate();
            client.BaseAddress = settings.GetBaseAddress();
            // Maxio uses HTTP Basic auth: API key as username, literal "x" as password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
