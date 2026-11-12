using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>The "Maxio:" configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
}

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds Maxio settings, refuses to start when a required one is missing, and registers the SDK client (singleton:
    /// options are captured once, so a rotated secret needs a restart) plus the application service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings
        {
            ApiKey = configuration[$"{MaxioSettings.SectionName}:ApiKey"],
            Subdomain = configuration[$"{MaxioSettings.SectionName}:Subdomain"],
            Environment = configuration[$"{MaxioSettings.SectionName}:Environment"],
            BaseUrl = configuration[$"{MaxioSettings.SectionName}:BaseUrl"],
        };

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it with user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it with user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.Environment))
            throw new InvalidOperationException("Maxio:Environment is not configured. Set it with user-secrets or your secret store (US or EU).");

        ServerEnvironment environment;
        if (string.Equals(settings.Environment.Trim(), ServerEnvironment.Us.Value, StringComparison.OrdinalIgnoreCase))
            environment = ServerEnvironment.Us;
        else if (string.Equals(settings.Environment.Trim(), ServerEnvironment.Eu.Value, StringComparison.OrdinalIgnoreCase))
            environment = ServerEnvironment.Eu;
        else
            throw new InvalidOperationException("Maxio:Environment must be US or EU.");

        const string clientName = "MaxioAdvancedBilling";
        services.AddHttpClient(clientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                // Writes are never resent by the SDK; the app reconciles instead.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(10),
                    HttpMethodsToRetry = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Options],
                },
                // Explicit factory: keeps MAXIO_ADVANCED_BILLING_LOG=trace from forcing request-body logging.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() },
            };

            if (environment == ServerEnvironment.Eu)
            {
                options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
