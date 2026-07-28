using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration of the Maxio Advanced Billing integration: settings binding, the typed HTTP
/// client (base address + HTTP Basic auth), and the subscription service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the Maxio integration. Settings are bound from the <c>Maxio</c> configuration
    /// section; credentials must be supplied via configuration (user-secrets / environment) and
    /// are never hard-coded. Validation is deferred until the client is first used so hosts that
    /// don't exercise billing (e.g. some tests) can still start.
    /// </summary>
    public static IServiceCollection AddMaxioIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddMemoryCache();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.ResolveBaseAddress();

            // Maxio uses HTTP Basic auth: username = API key, password = the literal "x".
            var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
