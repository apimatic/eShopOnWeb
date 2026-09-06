using System;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and wires up the subscription capability.
    /// </summary>
    /// <remarks>
    /// Configuration is validated on start-up, so a missing API key or product family fails the
    /// host rather than the first shopper request.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.AddMemoryCache();
        services.AddSingleton<SubscriberGate>();

        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddTransient<MaxioResilienceHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
            })
            // Order matters: the resilience handler is outermost, so every retry passes back
            // through the auth handler and is signed with the current API key.
            .AddHttpMessageHandler<MaxioResilienceHandler>()
            .AddHttpMessageHandler<MaxioAuthenticationHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            // Recycle pooled connections so DNS changes on the Maxio side are picked up.
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
