using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio subscription capability: settings, a typed HTTP client with Basic auth
/// pre-configured, and the orchestration service. Configuration is read from the "Maxio" section.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.ConfigurationSection);
        var settings = new MaxioSettings
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            Subdomain = section["Subdomain"] ?? string.Empty,
            ProductFamilyHandle = section["ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = section["BaseUrl"]
        };
        services.AddSingleton(settings);

        // Family-id resolution is cached here; harmless if the host also registers memory cache.
        services.AddMemoryCache();

        services.AddHttpClient<IMaxioGateway, MaxioApiClient>((serviceProvider, client) =>
        {
            var maxio = serviceProvider.GetRequiredService<MaxioSettings>();
            client.BaseAddress = maxio.ResolveBaseAddress();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio auth: HTTP Basic with the API key as the username and the literal "x" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxio.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
