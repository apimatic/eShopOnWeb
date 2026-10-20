using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    // Per-attempt bound; the whole-call budget is MaxioSubscriptionService.CallBudget.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio client and subscription service. Throws immediately (host refuses to start)
    /// when a required <c>Maxio:</c> setting is missing or blank. Settings are captured once, so a
    /// rotated key takes effect on restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        if (!MaxioSettings.IsValid(settings, out var error))
        {
            throw new InvalidOperationException(error);
        }

        services.AddSingleton(Options.Create(settings));
        services.AddHttpClient("Maxio", c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    Password = "x" // protocol constant: Maxio's Basic auth password is always "x"
                },
                // Writes (POST) are never resent by the SDK; the service reconciles ambiguous writes itself.
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Explicit factory: disables the env-var override; request bodies (PII) stay unlogged.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio");
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
