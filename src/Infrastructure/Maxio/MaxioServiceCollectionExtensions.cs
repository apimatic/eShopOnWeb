using System;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration of the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Adds subscription billing backed by Maxio Advanced Billing, bound from the "Maxio"
    /// configuration section.
    /// <para>
    /// Registration never fails on missing configuration: subscription billing is an additive
    /// capability, so a host without Maxio settings still starts and serves everything else -
    /// only the subscription endpoints report that they are unavailable.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services
            .AddHttpClient<IMaxioApiClient, MaxioApiClient>(client =>
            {
                // Timeouts (and retries) are enforced per attempt by MaxioRetryHandler.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Reports the state of the Maxio configuration once, at startup, so a misconfigured host
    /// is obvious from the logs instead of only failing on the first shopper request.
    /// </summary>
    public static void LogMaxioConfiguration(this IServiceProvider services, ILogger logger)
    {
        var options = services.GetRequiredService<IOptions<MaxioOptions>>().Value;
        var errors = options.Validate();

        if (errors.Count > 0)
        {
            logger.LogWarning(
                "Maxio subscription billing is not configured; the subscription endpoints will return 503. Problems: {Problems}",
                string.Join(" ", errors));
            return;
        }

        logger.LogInformation(
            "Maxio subscription billing configured for product family '{ProductFamilyHandle}' at {BaseAddress} ({Environment}).",
            options.ProductFamilyHandle,
            options.ResolveBaseAddress(),
            options.Environment);
    }
}
