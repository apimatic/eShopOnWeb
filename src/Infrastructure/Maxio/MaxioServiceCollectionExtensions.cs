using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: options binding, the resilient typed
/// HTTP client (base address + Basic auth derived from configuration), and the
/// <see cref="ISubscriptionService"/> implementation.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.ConfigSection));

        services.AddTransient<MaxioTransientErrorHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            options.Validate();

            client.BaseAddress = new Uri(options.ResolveBaseUrl() + "/");
            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<MaxioTransientErrorHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
