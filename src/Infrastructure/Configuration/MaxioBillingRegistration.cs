using System;
using System.Net.Http.Headers;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription module in a host's composition root (plan §2.1 / §4.3). Both the Web
/// storefront and the PublicApi call this, so the provider is still touched in exactly one class.
/// </summary>
public static class MaxioBillingRegistration
{
    /// <summary>The <c>User-Agent</c> the integration identifies itself with.</summary>
    private const string UserAgent = "eShopOnWeb-Subscribe";

    /// <summary>
    /// Binds <see cref="MaxioSettings"/>, registers the typed billing <see cref="System.Net.Http.HttpClient"/>
    /// and the subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Required configuration is checked while the host starts, so a deployment missing its
        // credential fails immediately and visibly instead of at the first customer request.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiKey),
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}' is required. Supply it through user-secrets or an environment variable.")
            .Validate(settings => settings.TryResolveBaseUrl(out _),
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)}' or '{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}' must be configured with a valid absolute http or https URL.")
            .ValidateOnStart();

        // The BaseAddress comes from configuration so the identical build can target production, a
        // sandbox tenant, or a local mock server. It is never hardcoded (plan §2.3).
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (settings.TryResolveBaseUrl(out var baseUrl) && baseUrl is not null)
            {
                httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }

            // A per-request ceiling in addition to the per-operation budget the client applies.
            httpClient.Timeout = settings.Timeout;
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }

    /// <summary>
    /// Registers the in-process notification pipeline for the subscription module on hosts that do
    /// not already have one. Safe to call after an existing <c>AddMediatR</c>.
    /// </summary>
    public static IServiceCollection AddSubscriptionNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SubscriptionService).Assembly));

        return services;
    }
}
