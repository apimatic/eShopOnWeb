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
    /// Longer than a healthy Maxio call needs, short enough that a stuck one does not hold an
    /// API request open. Maxio itself cuts requests off at 120 seconds.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound to the
    /// <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Registration deliberately does not fail when the section is missing. Subscriptions are an
    /// additive capability, and an unconfigured deployment should still start and serve the rest
    /// of the API; the subscription endpoints report themselves unavailable instead.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        services.AddMemoryCache();
        services.AddSingleton<MaxioSubscriberLocks>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(MaxioApiClient.HttpClientName, (serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

            if (!settings.IsConfigured)
            {
                // Leave the client unusable rather than throwing here: the service checks the
                // configuration before it ever issues a request.
                return;
            }

            client.BaseAddress = settings.ResolveBaseAddress();

            // Maxio authenticates with HTTP Basic: API key as the username, "X" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(settings.ApiKey + ":X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        })
        .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
