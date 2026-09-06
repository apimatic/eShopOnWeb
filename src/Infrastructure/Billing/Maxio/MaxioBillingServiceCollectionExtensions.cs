using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    private const string UserAgent = "eShopOnWeb-MaxioBilling/1.0";

    /// <summary>
    /// Wires up subscription billing from the <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Settings are validated lazily, on first use, rather than at startup: a host that never calls
    /// a subscription endpoint should still start, and a host that is missing its Maxio credentials
    /// should answer those endpoints with a clear <c>503</c> naming the missing keys instead of
    /// failing to boot.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();

        // Process-wide, so every request for a given shopper contends on the same lock.
        services.TryAddSingleton<KeyedAsyncLock>();

        services
            .AddHttpClient<IMaxioApiClient, MaxioApiClient>(ConfigureHttpClient)
            .AddHttpMessageHandler(serviceProvider => new MaxioRetryHandler(
                serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value.MaxRetryAttempts,
                serviceProvider.GetRequiredService<ILogger<MaxioRetryHandler>>()));

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void ConfigureHttpClient(IServiceProvider serviceProvider, System.Net.Http.HttpClient httpClient)
    {
        // Throws BillingConfigurationException when the Maxio settings are absent or invalid; that
        // surfaces to the caller as 503 rather than as an opaque failure deeper in the call.
        var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

        // Trailing slash so relative request paths append rather than replace the last segment;
        // this preserves any path in a Maxio:BaseUrl override.
        httpClient.BaseAddress = new Uri(options.ResolveBaseAddress() + "/");
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        // Maxio authenticates with HTTP Basic: the API key as the username and the literal "x" as
        // the password. See https://developers.maxio.com/http/getting-started/authentication.
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
