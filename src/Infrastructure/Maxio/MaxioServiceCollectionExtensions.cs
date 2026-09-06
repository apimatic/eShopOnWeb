using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration for the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed subscription plan catalog and subscription service.
    /// </summary>
    /// <remarks>
    /// Settings are validated lazily rather than at startup, on purpose. A host that is not
    /// configured for billing still starts and still serves every other endpoint; only the
    /// subscription endpoints answer 503, with a message naming the settings that are missing.
    /// A warning is written at startup so the condition is not silent.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MaxioOptions.SectionName);
        services.Configure<MaxioOptions>(section);

        services.AddMemoryCache();
        services.TryAddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptionsMonitor<MaxioOptions>>().CurrentValue;

                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("eShopOnWeb", ProductVersion));
            })
            // The retry handler sits outermost so a retried attempt gets a freshly built
            // Authorization header, which keeps key rotation working mid-flight.
            .AddHttpMessageHandler<MaxioRetryHandler>()
            .AddHttpMessageHandler<MaxioAuthenticationHandler>();

        services.AddScoped<ISubscriptionPlanCatalog, MaxioSubscriptionPlanCatalog>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Logs how the Maxio integration is configured, without ever revealing the credential.
    /// </summary>
    public static void LogMaxioConfiguration(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Maxio");
        var options = provider.GetRequiredService<IOptionsMonitor<MaxioOptions>>().CurrentValue;
        var problems = options.Validate().ToList();

        if (problems.Count > 0)
        {
            logger.LogWarning(
                "Subscription billing is not configured and its endpoints will answer 503. {Problems}",
                string.Join(" ", problems));

            return;
        }

        logger.LogInformation(
            "Subscription billing targets {BaseAddress} using product family '{ProductFamilyHandle}' "
            + "and collection method '{PaymentCollectionMethod}'.",
            options.ResolveBaseAddress(),
            options.ProductFamilyHandle,
            options.PaymentCollectionMethod);
    }

    private static string ProductVersion =>
        typeof(MaxioServiceCollectionExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "1.0.0";
}
