using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings binding, the typed HTTP client
/// (base address + HTTP Basic auth derived from configuration), and the billing service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind settings from the "Maxio" section. Validation is deferred to first use (when a
        // subscription endpoint is actually called) rather than at startup, so the host still boots
        // in environments where Maxio is not configured (e.g. unrelated integration tests).
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddSingleton<ReferenceLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
        {
            var settings = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.ResolveBaseAddress();

            // HTTP Basic auth: username = API key, password = "x" (per the spec's BasicAuth scheme).
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
