using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the Maxio Advanced Billing subscription integration: binds the "Maxio" settings section,
/// registers a typed <see cref="MaxioApiClient"/> HttpClient (base address + Basic auth derived from
/// settings), and the <see cref="ISubscriptionService"/> implementation.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.ConfigSection).Get<MaxioSettings>() ?? new MaxioSettings();
        services.AddSingleton(settings);

        services.AddHttpClient<MaxioApiClient>(client =>
        {
            // When settings are absent the client stays unconfigured; ISubscriptionService raises a
            // clear MaxioConfigurationException before any request is attempted, so the app still boots.
            if (settings.IsConfigured)
            {
                client.BaseAddress = settings.ResolveBaseUri();
                string token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
