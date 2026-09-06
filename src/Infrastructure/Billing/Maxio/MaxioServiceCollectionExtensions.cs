using System;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the typed <see cref="System.Net.Http.HttpClient"/> used for Maxio calls.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers
    /// <see cref="ISubscriptionService"/>.
    /// </summary>
    /// <remarks>
    /// When the section is absent or incomplete the capability registers a stand-in that fails
    /// every call with a clear message, instead of preventing the host from starting. When the
    /// section is present it is validated eagerly, so a misconfigured billing integration is caught
    /// at startup rather than on a shopper's first click.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MaxioSettings.SectionName);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        if (!settings.IsConfigured)
        {
            services.AddSingleton<ISubscriptionService>(_ => new UnconfiguredSubscriptionService(
                $"Subscription billing is not configured. Set '{MaxioSettings.SectionName}:ApiKey' and " +
                $"'{MaxioSettings.SectionName}:Subdomain' (or '{MaxioSettings.SectionName}:BaseUrl') and " +
                $"'{MaxioSettings.SectionName}:ProductFamilyHandle'."));

            return services;
        }

        // Reports every invalid setting at once rather than one per restart.
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.AddOptions<MaxioSettings>()
            .Bind(section)
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<MaxioCatalogCache>();
        services.AddSingleton(new MaxioRequestGate(settings.MaxConcurrentRequests));
        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (provider, client) =>
        {
            var current = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = current.ResolveBaseAddress();
            client.Timeout = TimeSpan.FromSeconds(current.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = MaxioApiClient.BuildBasicAuthHeader(current.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
    {
        public ValidateOptionsResult Validate(string? name, MaxioSettings options)
        {
            var failures = options.Validate();
            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures.ToList());
        }
    }
}
