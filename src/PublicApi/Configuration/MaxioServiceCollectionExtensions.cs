using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings binding, the authenticated typed
/// HTTP client, and the subscription service. Settings come from the <c>Maxio</c> configuration
/// section (populated via user-secrets / environment); no values are hard-coded here.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSectionName))
            .Validate(settings =>
            {
                // Throws with a specific, actionable message when required settings are missing.
                settings.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddHttpClient<MaxioClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseUri();

            // HTTP Basic auth over TLS: API key as username, "X" as password.
            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio enforces a 120s request cut-off; stay just under it.
            client.Timeout = TimeSpan.FromSeconds(110);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
