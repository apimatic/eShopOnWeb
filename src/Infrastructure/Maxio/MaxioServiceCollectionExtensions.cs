using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration of the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio, bound from the <c>Maxio</c> configuration
    /// section.
    /// </summary>
    /// <remarks>
    /// Missing configuration is reported as a startup warning rather than a startup failure: the
    /// subscription endpoints then answer 503 while the rest of eShopOnWeb - catalog, basket and
    /// orders - keeps working, since subscriptions are an additive capability.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.ConfigurationSectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMemoryCache();
        services.AddSingleton<SubscriberLockProvider>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        services.AddSingleton<IMaxioConfigurationReport>(_ => new MaxioConfigurationReport(settings));

        return services;
    }

    /// <summary>
    /// Writes a single, unambiguous line at startup saying whether subscription billing is live and
    /// which site it points at. The API key is never logged.
    /// </summary>
    public static void LogMaxioConfiguration(this IServiceProvider services, ILogger logger)
    {
        var report = services.GetService<IMaxioConfigurationReport>();
        if (report is null)
        {
            return;
        }

        if (report.IsConfigured)
        {
            logger.LogInformation(
                "Maxio subscription billing is configured. Base address: {BaseAddress}. Product family: {ProductFamilyHandle}.",
                report.BaseAddress, report.ProductFamilyHandle);
        }
        else
        {
            logger.LogWarning(
                "Maxio subscription billing is NOT configured, so the subscription endpoints will answer 503. {Problems}",
                string.Join(" ", report.Problems));
        }
    }
}

/// <summary>
/// Startup-time view of the Maxio configuration, with no secret material on it.
/// </summary>
public interface IMaxioConfigurationReport
{
    bool IsConfigured { get; }

    string? BaseAddress { get; }

    string? ProductFamilyHandle { get; }

    System.Collections.Generic.IReadOnlyList<string> Problems { get; }
}

internal sealed class MaxioConfigurationReport : IMaxioConfigurationReport
{
    public MaxioConfigurationReport(MaxioSettings settings)
    {
        Problems = settings.Validate();
        IsConfigured = Problems.Count == 0;
        ProductFamilyHandle = settings.ProductFamilyHandle;
        BaseAddress = IsConfigured ? settings.ResolveBaseAddress() : null;
    }

    public bool IsConfigured { get; }

    public string? BaseAddress { get; }

    public string? ProductFamilyHandle { get; }

    public System.Collections.Generic.IReadOnlyList<string> Problems { get; }
}
