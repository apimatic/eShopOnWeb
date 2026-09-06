using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the subscription capability backed by Maxio Advanced Billing.
    /// </summary>
    /// <remarks>
    /// Settings are validated the first time they are read rather than at startup, so a host with no
    /// Maxio section still boots and serves the rest of eShopOnWeb; the subscription endpoints are
    /// the only thing that fails, and they say why.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // Backs the cache of the billing site's own settings, which every subscribe call reads.
        services.AddMemoryCache();

        // One lock registry for the whole process, so concurrent subscribes from the same shopper
        // meet each other regardless of which request scope they arrived on.
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddHttpClient<IBillingGateway, MaxioBillingGateway>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseAddress();
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb/1.0 (+maxio-subscriptions)");

            // Maxio authenticates with HTTP Basic: the site API key as the user name, "X" as the password.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X")));
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        return services;
    }
}
