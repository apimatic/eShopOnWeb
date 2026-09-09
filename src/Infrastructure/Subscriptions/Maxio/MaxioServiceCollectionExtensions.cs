using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Registers the Maxio-backed subscription billing capability: settings (bound from the <c>Maxio:</c>
/// section), the typed API client with Basic auth, and the <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind settings from the Maxio: section. Validation runs lazily (on first use of the options), so a
        // host with no Maxio configuration still starts — only calls into the subscription feature will fail.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                "Maxio:ApiKey is required (set it from the MAXIO_API_KEY environment variable via user-secrets).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.BaseUrl) || !string.IsNullOrWhiteSpace(s.Subdomain),
                "Either Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required (set it from MAXIO_DEFAULT_PRODUCT_FAMILY).");

        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseUrl();
            client.Timeout = TimeSpan.FromSeconds(30);

            // HTTP Basic auth: username = API key, password = the literal "x" (per the spec's securityScheme).
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
