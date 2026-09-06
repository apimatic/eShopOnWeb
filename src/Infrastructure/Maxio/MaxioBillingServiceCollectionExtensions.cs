using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires up the Maxio-backed subscription capability.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the subscription service, the Maxio gateway and the typed HTTP client, binding
    /// settings from the <c>Maxio</c> configuration section.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddSingleton<MaxioSiteCache>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(ConfigureClient)
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<IBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, System.Net.Http.HttpClient httpClient)
    {
        var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

        // A misconfigured deployment must not blow up client construction: the gateway reports the
        // problem as a 503 with the specific settings that are missing.
        if (!options.IsConfigured)
        {
            return;
        }

        httpClient.BaseAddress = options.ResolveBaseAddress();

        // The spec's only security scheme is BasicAuth: "the username is a Maxio Chargify API key,
        // the password is x".
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }
}
