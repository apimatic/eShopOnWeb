using System;
using Microsoft.eShopWeb.ApplicationCore.Billing.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers the Maxio-backed subscription billing capability.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Name of the logical <see cref="System.Net.Http.HttpClient"/> used for Maxio calls.
    /// </summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Binds <see cref="MaxioOptions"/> from the <c>Maxio</c> configuration section and wires the
    /// typed API client, its handler pipeline and the subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MaxioOptions.SectionName);

        var optionsBuilder = services.AddOptions<MaxioOptions>()
            .Bind(section)
            .ValidateDataAnnotations();

        // Fail fast when the capability is configured, so a bad deployment is caught at startup
        // rather than on the first shopper request. When the section is absent the host still
        // boots (the Web storefront and the API test host do not carry Maxio credentials) and the
        // subscription endpoints answer 503 with a message naming the missing settings.
        if (section.Exists())
        {
            optionsBuilder.ValidateOnStart();
        }

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.AddMemoryCache();

        // One instance for the whole process: the lock only has meaning if every subscribe
        // request contends on the same stripes.
        services.AddSingleton<StripedAsyncLock>(_ => new StripedAsyncLock());

        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddTransient<MaxioResilienceHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
            })
            // Order matters: the resilience handler is outermost so a retry re-runs
            // authentication and therefore always sends the current API key.
            .AddHttpMessageHandler<MaxioResilienceHandler>()
            .AddHttpMessageHandler<MaxioAuthenticationHandler>()
            .RedactLoggedHeaders(new[] { "Authorization" });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
