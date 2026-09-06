using System;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Overall budget for one Maxio call, including any retries.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers the API client and the
    /// <see cref="ISubscriptionService"/> implementation built on it.
    /// <para>
    /// Missing configuration is not fatal at start-up: the rest of the application keeps working
    /// and the subscription endpoints report that billing is unavailable. That keeps a deployment
    /// without Maxio credentials - a local run, or the automated tests - usable.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(client =>
            {
                // The base address is resolved per request from configuration, so that a changed
                // subdomain or base URL takes effect without recreating the client.
                client.Timeout = HttpClientTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            // Outermost first: retries wrap authentication, so every attempt is signed afresh.
            .AddHttpMessageHandler<MaxioRetryHandler>()
            .AddHttpMessageHandler<MaxioAuthenticationHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Writes a start-up note about the state of the Maxio configuration. Secret values are never
    /// logged - only whether they are present.
    /// </summary>
    public static void LogSubscriptionBillingConfiguration(this IServiceProvider services, ILogger logger)
    {
        var options = services.GetRequiredService<IOptions<MaxioOptions>>().Value;
        var errors = options.Validate();

        if (errors.Count > 0)
        {
            logger.LogWarning(
                "Maxio subscription billing is not configured ({Problems}); the subscription endpoints will report 503 until it is.",
                string.Join(" ", errors));

            return;
        }

        logger.LogInformation(
            "Maxio subscription billing is configured against {BaseUrl} for product family {ProductFamily}.",
            options.ResolveBaseUrl(), options.ProductFamilyHandle);
    }
}
