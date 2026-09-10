using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio-backed subscription-billing capability: settings binding, the typed
/// HTTP client (Basic auth + base address), the gateway, and the application service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IMaxioGateway, MaxioGateway>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.ResolveBaseAddress();

            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio enforces a 120s server-side cut-off; keep the client a touch above it.
            client.Timeout = TimeSpan.FromSeconds(125);
        });

        // Shared across scopes so per-user serialization actually coordinates concurrent requests.
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
