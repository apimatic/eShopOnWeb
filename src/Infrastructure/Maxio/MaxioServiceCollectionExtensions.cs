using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers recurring-subscription billing backed by Maxio Advanced Billing, bound from the
    /// "Maxio" configuration section.
    /// <para>
    /// Registration never fails on missing credentials: hosts that do not have them configured stay
    /// startable, and the subscription endpoints report the misconfiguration when they are called.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(ConfigureClient)
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider provider, System.Net.Http.HttpClient client)
    {
        var settings = provider.GetRequiredService<IOptionsMonitor<MaxioSettings>>().CurrentValue;

        try
        {
            client.BaseAddress = MaxioEnvironments.ResolveBaseAddress(settings);
        }
        catch (SubscriptionBillingConfigurationException)
        {
            // Leave the client unusable rather than failing startup. Every service entry point
            // validates the settings first and reports the problem with the offending keys named.
        }

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // openapi.yaml, components.securitySchemes.BasicAuth:
            // "The `username` is a Maxio Chargify API key. The `password` is `x`."
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey.Trim()}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

        // Per-attempt timeouts and the overall retry budget are enforced by MaxioRetryHandler, which
        // would otherwise be cut short by HttpClient's own single timeout across all attempts.
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
}
