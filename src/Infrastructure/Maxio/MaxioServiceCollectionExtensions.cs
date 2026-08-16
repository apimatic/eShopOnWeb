using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// DI wiring for the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio typed HTTP client and the <see cref="ISubscriptionBillingService"/>.
    /// The <paramref name="settings"/> are validated up front so misconfiguration fails fast.
    /// Authentication follows the spec's BasicAuth scheme (API key as username, "x" as password)
    /// and the base address follows the spec's server templating (Maxio:BaseUrl overrides the
    /// subdomain-derived URL).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, MaxioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Registration is intentionally tolerant of missing configuration so the host always
        // boots (e.g. in tests that exercise unrelated endpoints). Configuration is validated
        // lazily the first time a subscription flow runs, via MaxioBillingService.
        services.AddSingleton(settings);
        services.AddTransient<MaxioTransientFaultHandler>();

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(client =>
        {
            client.BaseAddress = settings.ResolveBaseUri();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
