using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Registers the Maxio Advanced Billing subscription integration: settings, a pre-authenticated
/// typed <see cref="System.Net.Http.HttpClient"/>, and the orchestrating service.
/// </summary>
public static class MaxioSubscriptionRegistration
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind settings from the "Maxio" section. Values come from user-secrets / environment
        // configuration and are intentionally absent from the repository. When unconfigured the
        // subscription endpoints return HTTP 503, leaving the rest of the API fully functional.
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.ConfigurationSection).Bind(settings);
        services.AddSingleton(settings);

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            if (!settings.IsConfigured)
            {
                // Base address is required by HttpClient even though calls will short-circuit
                // with a configuration error; use a harmless placeholder.
                client.BaseAddress = new Uri("https://maxio.invalid/");
                return;
            }

            client.BaseAddress = new Uri(settings.ResolveBaseUrl());

            // HTTP Basic auth: API key as username, "X" as password (per Maxio API docs).
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio enforces a 120s server-side cut-off; keep the client timeout aligned.
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
