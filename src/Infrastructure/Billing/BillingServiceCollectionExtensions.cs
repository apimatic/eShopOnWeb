using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Registers recurring subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary>Maxio authenticates with HTTP Basic: the API key is the user name and the password is a literal "x".</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>
    /// Adds <see cref="ISubscriptionService"/> and the Maxio client it runs on, configured from the
    /// <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Registration never touches the network and never fails on missing configuration, so an
    /// application that has not been given Maxio credentials still starts and serves everything else.
    /// The settings are validated when the client is first constructed -- see <see cref="MaxioSettingsValidator"/>.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        // Process-wide so that concurrent requests from the same shopper -- which land on different
        // scopes -- actually contend for the same lock and share the same catalog snapshot.
        services.AddSingleton<KeyedAsyncLock>();
        services.AddSingleton(serviceProvider => new AsyncTtlCache<MaxioPlanCatalog>(
            serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value.CatalogCacheDuration));

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
                MaxioSettingsValidator.EnsureValid(settings);

                client.BaseAddress = settings.ResolveBaseAddress();

                // Covers the whole call including any retries the handler below performs.
                client.Timeout = settings.Timeout;

                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{settings.ApiKey}:{ApiKeyPasswordPlaceholder}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
