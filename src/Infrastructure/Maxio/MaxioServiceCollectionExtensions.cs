using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Wires up subscription billing against Maxio, binding options from the "Maxio" configuration section.
    /// </summary>
    /// <remarks>
    /// Registration deliberately succeeds even when Maxio is not configured: the host must still start
    /// (the rest of eShopOnWeb does not depend on billing), and the missing configuration is reported
    /// per-request as a <see cref="ApplicationCore.Exceptions.BillingConfigurationException"/> instead.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services)
    {
        // Bound on first use rather than here, so every configuration source the host ends up with -
        // user-secrets, environment variables, a test host's overrides - has been applied by then.
        services.AddSingleton(serviceProvider =>
        {
            var settings = new MaxioSettings();
            serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(MaxioSettings.SectionName)
                .Bind(settings);
            return settings;
        });

        services.AddSingleton<KeyedAsyncLock>();
        services.AddSingleton(serviceProvider =>
            new MaxioConcurrencyLimiter(serviceProvider.GetRequiredService<MaxioSettings>().MaxConcurrentRequests));
        services.AddTransient<MaxioConcurrencyHandler>();
        services.AddTransient<MaxioResilienceHandler>();
        services.AddMemoryCache();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<MaxioSettings>();

                // The timeout is the budget for the whole call including retries, so a slow billing
                // system degrades into a fast 503 rather than an API request that hangs.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

                if (settings.TryResolveBaseAddress(out var baseAddress))
                {
                    client.BaseAddress = baseAddress;
                }

                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    // Maxio authenticates with HTTP Basic: the API key as the username, "X" as the password.
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey.Trim()}:X"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }
            })
            .AddHttpMessageHandler<MaxioResilienceHandler>()
            .AddHttpMessageHandler<MaxioConcurrencyHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
