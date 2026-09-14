using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Logical name of the typed HTTP client used to talk to Maxio.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Adds subscription billing backed by Maxio, bound from the <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Registration never fails on missing configuration: a deployment without Maxio credentials still
    /// starts, and the subscription endpoints answer <c>503</c> with the names of the missing keys.
    /// That keeps the rest of eShopOnWeb, and its test suites, independent of billing configuration.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider =>
            new MaxioReferenceFactory(provider.GetRequiredService<IOptions<MaxioSettings>>().Value.ReferencePrefix));

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptionsMonitor<MaxioSettings>>().CurrentValue;

                if (settings.IsConfigured)
                {
                    client.BaseAddress = settings.ResolveBaseAddress();

                    // Maxio authenticates with HTTP Basic over TLS: the API key is the username and
                    // the password is the literal "X".
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X")));
                }

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

                // Timeouts are enforced per attempt by MaxioRetryHandler, so the pipeline as a whole is
                // free to spend time on backoff.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<MaxioSubscriptionService>();
        services.AddScoped<ISubscriptionPlanService>(provider => provider.GetRequiredService<MaxioSubscriptionService>());
        services.AddScoped<ISubscriptionService>(provider => provider.GetRequiredService<MaxioSubscriptionService>());

        return services;
    }
}
