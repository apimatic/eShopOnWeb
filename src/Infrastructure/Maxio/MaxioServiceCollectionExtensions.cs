using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" configuration section and wires up the billing service.
    /// </summary>
    /// <remarks>
    /// Registration never fails on missing configuration: the app must still start so its other
    /// endpoints work, and so the subscription endpoints can report a clear configuration error
    /// instead of the host refusing to boot. Configuration problems are reported at first use and
    /// logged once at startup.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                if (options.Validate().Count == 0)
                {
                    client.BaseAddress = options.ResolveBaseAddress();
                }

                // Spec security scheme BasicAuth: "The username is a Maxio Chargify API key. The
                // password is x." Sent pre-emptively so no request costs an extra challenge round trip.
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>
    /// Writes a single startup line stating whether the integration is configured, so a
    /// missing secret is obvious in the log rather than only when a shopper hits an endpoint.
    /// </summary>
    public static IServiceProvider LogMaxioConfigurationStatus(this IServiceProvider provider, ILogger logger)
    {
        var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
        var failures = options.Validate();

        if (failures.Count > 0)
        {
            logger.LogWarning(
                "Maxio subscription billing is not configured; the subscription endpoints will return 503. {Failures}",
                string.Join(" ", failures));
        }
        else
        {
            // Base address and family handle only. The API key is never logged.
            logger.LogInformation(
                "Maxio subscription billing configured against {BaseAddress} (product family '{ProductFamilyHandle}').",
                options.ResolveBaseAddress(),
                options.ProductFamilyHandle);
        }

        return provider;
    }
}
