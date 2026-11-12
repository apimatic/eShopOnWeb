using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The "Maxio" configuration section. No value has a default; they come from configuration
/// (user-secrets locally, the deployment's secret store elsewhere).
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        Validate(settings);

        services.AddSingleton(settings);
        services.AddSingleton(sp =>
        {
            var options = BuildOptions(settings, sp.GetRequiredService<ILoggerFactory>());
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            return new MaxioAdvancedBillingClient(http, options);
        });
        services.AddSingleton<MaxioSiteCache>();
        services.AddScoped<MaxioGateway>();
        services.AddScoped<ClaimStore>();
        services.AddScoped<BillingService>();
        return services;
    }

    /// <summary>Refuses to start when a setting is missing or blank.</summary>
    public static void Validate(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it with user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it with user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.Environment))
            throw new InvalidOperationException("Maxio:Environment is not configured. Set it with user-secrets or your secret store.");
        ParseEnvironment(settings.Environment);
    }

    private static ServerEnvironment ParseEnvironment(string value)
    {
        if (string.Equals(value.Trim(), "US", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Us;
        if (string.Equals(value.Trim(), "EU", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Eu;
        throw new InvalidOperationException("Maxio:Environment must be 'US' or 'EU'.");
    }

    public static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ParseEnvironment(settings.Environment!),
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
            // Explicit factory with body logging off: the "MAXIO_ADVANCED_BILLING_LOG" variable cannot switch bodies on.
            Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false },
        };
        options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        return options;
    }
}
