using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioServiceCollectionExtensions
{
    // Bounds ONE attempt (SDK Retry.Timeout and HttpClient.Timeout); the whole call is bounded in MaxioSubscriptionService.
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(8);

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)} is not configured (set it via user-secrets or your secret store).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain), $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)} is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is not configured.")
            .Validate(o => string.IsNullOrWhiteSpace(o.BaseUrl) || Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)} must be an absolute URL when set.")
            .ValidateOnStart();

        // Options are read once here: a rotated secret takes effect on restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                // Maxio basic auth: username = API key, password = "x" (sdk-map.md, Servers & auth).
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Explicit factory: no *_LOG env-var override; bodies/headers stay off (customer PII).
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
            };
            options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;

            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = PerAttemptTimeout,
            };
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
