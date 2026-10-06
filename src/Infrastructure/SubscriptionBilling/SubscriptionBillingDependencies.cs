using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public static class SubscriptionBillingDependencies
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client. All configuration comes from the
    /// "Maxio" section (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, and the
    /// optional Maxio:BaseUrl override); nothing is hard-coded, so the same build runs
    /// against any Maxio site and catalog. Credentials are validated lazily per request,
    /// not at startup, so an unconfigured host can still boot (endpoints then answer
    /// 503 with a configuration hint).
    /// </summary>
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SubscriptionBillingSettings>(configuration.GetSection(SubscriptionBillingSettings.SECTION_NAME));

        services.AddHttpClient<ISubscriptionBillingClient, MaxioSubscriptionBillingClient>((sp, httpClient) =>
        {
            var settings = sp.GetRequiredService<IOptions<SubscriptionBillingSettings>>().Value;
            settings.EnsureConfigured();

            httpClient.BaseAddress = new Uri(settings.GetApiBaseUrl().TrimEnd('/') + "/");
            httpClient.Timeout = TimeSpan.FromSeconds(60);

            // Maxio authenticates with HTTP Basic over TLS: API key as username, "X" as password.
            var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey!.Trim()}:X"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("eShopOnWeb", "1.0"));
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(Maxio-Subscription-Billing)"));
        });

        return services;
    }
}
