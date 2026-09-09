using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration for the Maxio Advanced Billing subscription integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="MaxioSettings"/> from the <c>Maxio:</c> configuration section and registers the typed
    /// Maxio HTTP client and the <see cref="ISubscriptionService"/> that orchestrates the subscription flow.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        var settings = new MaxioSettings
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            Subdomain = section["Subdomain"] ?? string.Empty,
            ProductFamilyHandle = section["ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = section["BaseUrl"],
        };

        services.AddSingleton(settings);

        // Maxio authenticates with HTTP Basic auth: username = API key, password = the literal "x".
        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));

        // Validation is deferred to first use (see MaxioSubscriptionService) so hosts that never touch the
        // subscription capability — e.g. the functional-test host — can start without Maxio configuration.
        // When settings are incomplete, use a non-routable placeholder base address; the service refuses to
        // call it and returns a clear configuration error instead.
        var baseUri = settings.IsConfigured
            ? settings.ResolveBaseUri()
            : new Uri("https://maxio-not-configured.invalid/");

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
