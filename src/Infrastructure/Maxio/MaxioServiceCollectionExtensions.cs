using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "Maxio";

    /// <summary>
    /// Registers the Maxio-backed subscription service. Settings are validated at host start,
    /// so a missing credential stops the app instead of surfacing as a 401 on the first call.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();

        // Named client: keeps this pipeline off the shared default HttpClient.
        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = MaxioSubscriptionService.AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5) // the SDK client below is a singleton; keep DNS fresh
            });

        // Built once, so a rotated secret takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = MaxioSubscriptionService.AttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Explicit factory: disables the env-var trace switch. Request bodies carry customer PII, so they stay unlogged.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                },
            };
            options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME), options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
