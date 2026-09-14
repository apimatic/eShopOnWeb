using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound to the <c>Maxio:</c>
    /// configuration section.
    /// <para>
    /// Registration deliberately never fails: a host with no Maxio credentials still starts, and the
    /// subscription endpoints report the capability as unavailable rather than taking the whole
    /// application down with them.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMemoryCache();

        // Singleton: the lock map coordinates subscribe attempts across all requests in the process.
        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                if (settings.IsConfigured)
                {
                    client.BaseAddress = settings.ResolveBaseAddress();

                    // Maxio uses HTTP Basic auth with the API key as the username and a literal "X"
                    // as the password.
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

                // The per-attempt budget lives in the resilience handler; this is the overall ceiling
                // covering every retry and the backoff between them.
                client.Timeout = TotalBudget(settings);
            })
            .AddHttpMessageHandler(provider =>
            {
                var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;
                return new MaxioResilienceHandler(
                    provider.GetRequiredService<ILogger<MaxioResilienceHandler>>(),
                    settings.MaxRetries,
                    settings.RetryBaseDelay,
                    settings.Timeout);
            });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static TimeSpan TotalBudget(MaxioSettings settings)
    {
        var attempts = Math.Max(1, settings.MaxRetries + 1);
        var backoff = TimeSpan.FromMilliseconds(settings.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempts));
        return settings.Timeout * attempts + backoff + TimeSpan.FromSeconds(5);
    }
}
