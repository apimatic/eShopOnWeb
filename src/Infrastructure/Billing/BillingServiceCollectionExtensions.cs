using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the
    /// "Maxio" configuration section.
    ///
    /// Registration never throws on missing settings: an unconfigured deployment still starts and
    /// serves the rest of the API, and only the subscription endpoints report the misconfiguration.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<MaxioRequestGate>();
        services.AddSingleton<SubscriberKeyedLock>();
        services.AddTransient<MaxioResilienceHandler>();

        services.AddHttpClient<MaxioApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;

                // Throws BillingConfigurationException when the credentials or site are missing,
                // which the API layer turns into a 503 with an actionable message.
                options.EnsureValid();

                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));

                // HTTP Basic with the API key as the username and "X" as the password.
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .AddHttpMessageHandler<MaxioResilienceHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
