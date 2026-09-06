using System;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Name of the <see cref="System.Net.Http.HttpClient"/> used to reach Maxio. Exposed so a test
    /// host can substitute the primary handler without reaching into internal types.
    /// </summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers
    /// <see cref="ISubscriptionBillingService"/> on top of it.
    /// </summary>
    /// <remarks>
    /// Configuration is validated on first use rather than at startup, so a host with no <c>Maxio</c>
    /// section still starts and serves the rest of the application; only the subscription endpoints
    /// report the misconfiguration.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddSingleton<MaxioSettingsProvider>();
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<MaxioSettingsProvider>().Current;

                if (settings.IsConfigured)
                {
                    client.BaseAddress = settings.ResolveBaseAddress();
                }

                // Per-attempt timeouts are enforced by MaxioTransientFaultHandler; a client-wide
                // timeout here would also have to cover the retries and their backoff.
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.Add(UserAgent());
            })
            .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static ProductInfoHeaderValue UserAgent()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        return new ProductInfoHeaderValue("eShopOnWeb", version);
    }
}
