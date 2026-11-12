using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }

    public ServerEnvironment ResolveEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment)) return ServerEnvironment.Us;
        return Environment.Trim().ToLowerInvariant() switch
        {
            "us" => ServerEnvironment.Us,
            "eu" => ServerEnvironment.Eu,
            _ => throw new InvalidOperationException("Maxio:Environment must be 'Us' or 'Eu'.")
        };
    }

    /// <summary>Throws (refusing to start the host) when a required part is missing or blank.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it with user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        ResolveEnvironment();
    }
}

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION).Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();

        services.AddSingleton(settings);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once at registration: a rotated secret needs a process restart.
        services.AddSingleton(sp =>
        {
            var environment = settings.ResolveEnvironment();
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
                // Explicit factory so the SDK's *_LOG environment variable cannot arm body logging.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() },
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim, as the API's base address.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
                options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
            }

            var http = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
