using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound to the "Maxio"
    /// configuration section.
    /// <para>
    /// Misconfiguration is reported when the capability is used rather than at startup: subscription
    /// billing is additive, and a host that has not been given Maxio credentials should still serve
    /// the rest of the API.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<MaxioConcurrencyLimiter>();
        services.AddTransient<MaxioConcurrencyHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                if (!string.IsNullOrWhiteSpace(settings.Subdomain) || !string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    client.BaseAddress = settings.ResolveBaseAddress();
                }

                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    // Maxio authenticates with HTTP Basic over TLS: the API key is the user name and
                    // "X" is the password.
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            })
            .AddHttpMessageHandler<MaxioConcurrencyHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
