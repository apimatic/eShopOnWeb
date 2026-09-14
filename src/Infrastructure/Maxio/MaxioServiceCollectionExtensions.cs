using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Per-attempt ceiling. The API itself cuts requests off at 120 seconds.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, configured from the
    /// <c>Maxio</c> configuration section.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // Process-wide state: the concurrency budget, the site configuration cache and the per-account
        // signup locks all have to be shared to be worth anything.
        services.AddSingleton<MaxioRequestThrottle>();
        services.AddSingleton<MaxioSiteCache>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioResilienceHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            httpClient.BaseAddress = settings.ResolveBaseAddress();
            httpClient.Timeout = RequestTimeout;

            // The API authenticates with HTTP Basic over TLS: the API key is the user name and the
            // password is a placeholder.
            var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.ApiKey + ":X"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
        })
        .AddHttpMessageHandler<MaxioResilienceHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
