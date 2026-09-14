using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds <c>Maxio:</c> settings and registers the billing service and its typed HttpClient.
    /// <para>
    /// Registration is boot-safe: if credentials are absent the app still starts, and only the
    /// subscription endpoints fail (with a clear "not configured" error) when actually invoked.
    /// This keeps the subscription capability additive to the rest of eShopOnWeb.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Read values individually (no binder dependency); values come from user-secrets/env.
        var settings = new MaxioSettings
        {
            ApiKey = configuration["Maxio:ApiKey"] ?? string.Empty,
            Subdomain = configuration["Maxio:Subdomain"] ?? string.Empty,
            ProductFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = configuration["Maxio:BaseUrl"]
        };

        services.AddSingleton(settings);

        services.AddHttpClient<MaxioApiClient>((serviceProvider, http) =>
        {
            var maxio = serviceProvider.GetRequiredService<MaxioSettings>();

            // Only configure the transport when we have enough settings; otherwise the service's
            // call-time guard produces a clear NotConfigured error.
            if (maxio.IsConfigured)
            {
                http.BaseAddress = new Uri(maxio.ResolveBaseUrl());

                // Maxio uses HTTP Basic auth: API key as username, "X" as password, over TLS.
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxio.ApiKey}:X"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio enforces a 120s request cut-off; keep the client aligned.
            http.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
