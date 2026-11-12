using System;
using System.Collections.Generic;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

/// <summary>
/// Settings bound from the "Maxio:" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override; used verbatim as the API base address when set.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Total time budget of one request through Maxio, in seconds.</summary>
    public int TotalTimeoutSeconds { get; set; } = 45;

    /// <summary>Page cap for paged reads (pages of 200).</summary>
    public int MaxPages { get; set; } = 50;

    public ServerEnvironment ParseEnvironment() =>
        string.Equals(Environment?.Trim(), "eu", StringComparison.OrdinalIgnoreCase) ? ServerEnvironment.Eu : ServerEnvironment.Us;

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            yield return $"{SECTION}:ApiKey is not configured";
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            yield return $"{SECTION}:Subdomain is not configured (required unless {SECTION}:BaseUrl is set)";
        var env = Environment?.Trim();
        if (!string.Equals(env, "us", StringComparison.OrdinalIgnoreCase) && !string.Equals(env, "eu", StringComparison.OrdinalIgnoreCase))
            yield return $"{SECTION}:Environment is not configured or is not 'us' or 'eu'";
        if (TotalTimeoutSeconds <= 0)
            yield return $"{SECTION}:TotalTimeoutSeconds must be positive";
        if (MaxPages <= 0)
            yield return $"{SECTION}:MaxPages must be positive";
    }
}

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>
    /// Binds Maxio settings, fails the host start when they are unusable, and registers one long-lived SDK client.
    /// Options are built once here; a rotated key needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION).Get<MaxioSettings>() ?? new MaxioSettings();
        var problems = new List<string>(settings.Validate());
        if (problems.Count > 0)
            throw new InvalidOperationException("Maxio configuration is invalid: " + string.Join("; ", problems));

        services.AddSingleton(settings);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            var environment = settings.ParseEnvironment();
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
                // PUT is dropped from the resent verbs: billing-date, product-change and reactivation writes must not be replayed.
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    HttpMethodsToRetry = [System.Net.Http.HttpMethod.Get, System.Net.Http.HttpMethod.Head, System.Net.Http.HttpMethod.Options]
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            var baseUrl = settings.BaseUrl;
            if (environment == ServerEnvironment.Eu)
            {
                if (!string.IsNullOrWhiteSpace(baseUrl)) options.Server.Production.Eu.BaseUrl = baseUrl;
                if (!string.IsNullOrWhiteSpace(settings.Subdomain)) options.Server.Production.Eu.Site = settings.Subdomain.Trim();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(baseUrl)) options.Server.Production.Us.BaseUrl = baseUrl;
                if (!string.IsNullOrWhiteSpace(settings.Subdomain)) options.Server.Production.Us.Site = settings.Subdomain.Trim();
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioClaims>();
        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
