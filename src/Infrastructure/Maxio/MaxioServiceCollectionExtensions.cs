using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers recurring-subscription billing backed by Maxio Advanced Billing, bound from the
    /// <c>Maxio</c> configuration section.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSection));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        services.AddMemoryCache();
        services.TryAddSingleton<KeyedAsyncLock>();
        services.TryAddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<MaxioApiClient>((provider, client) =>
            {
                var settings = MaxioOptionsAccessor.Resolve(provider.GetRequiredService<IOptions<MaxioSettings>>());

                client.BaseAddress = settings.ResolveBaseAddress();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Maxio authenticates with HTTP Basic: the API key is the user name and the
                // password is the literal "X".
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                // Maxio cuts requests off at 120s; fail faster than that so a stuck call cannot
                // hold an eShopOnWeb request open, and let the retry handler decide what to replay.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        services.AddHostedService<MaxioConfigurationReporter>();

        return services;
    }
}
