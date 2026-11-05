using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio subscription billing. Settings come from the "Maxio:" section and are validated on host start.
    /// The SDK client is built once (singleton): a rotated API key takes effect on the next process start.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        services.TryAddSingleton(sp => CreateClient(sp.GetRequiredService<IOptions<MaxioSettings>>().Value, sp.GetRequiredService<ILoggerFactory>()));
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }

    private static MaxioAdvancedBillingClient CreateClient(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            // Per-attempt timeout; the whole-call budget is enforced by the service. POST is not in
            // HttpMethodsToRetry by default, so the SDK never resends a write.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10), MaxRetries = 2 },
            Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false },
        };
        options.Server.Production.Us.Site = settings.Subdomain!;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        }

        // Recycle pooled connections so a long-lived singleton still follows DNS changes.
        var httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
