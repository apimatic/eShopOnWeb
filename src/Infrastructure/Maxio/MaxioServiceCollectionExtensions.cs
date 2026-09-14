using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires the Maxio Advanced Billing implementation of <see cref="ISubscriptionService"/> into the
/// application's service collection.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    private const string UserAgent = "eShopOnWeb-Subscriptions";

    /// <summary>
    /// Registers subscription billing backed by Maxio, bound from the <c>Maxio</c> configuration
    /// section.
    /// </summary>
    /// <remarks>
    /// Settings are validated lazily rather than at start-up so a host without billing configured
    /// still boots; the failure then surfaces only on the subscription endpoints.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        services.AddMemoryCache();
        services.TryAddSingleton<KeyedAsyncLock>();
        services.TryAddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(ConfigureClient)
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, System.Net.Http.HttpClient client)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

        client.BaseAddress = settings.ResolveBaseAddress();
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

        // Maxio authenticates exclusively with HTTP Basic: the API key is the user name and the
        // password is the literal "x".
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
