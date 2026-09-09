using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration of the Maxio subscription-billing integration. Binds the <c>Maxio:</c> configuration
/// section, configures a typed <see cref="MaxioApiClient"/> (base address + HTTP Basic auth), and
/// registers the <see cref="ISubscriptionService"/> implementation.
/// </summary>
public static class MaxioServiceExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.SectionName).Bind(settings);
        settings.Validate();

        services.AddSingleton(settings);

        var baseUri = settings.ResolveBaseUri();
        // HTTP Basic auth: API key as username, literal "X" as password.
        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X"));

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(client =>
        {
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Maxio enforces a 120s server-side cut-off; align the client timeout.
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
