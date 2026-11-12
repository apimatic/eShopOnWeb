using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Bound from the "Maxio:" configuration section.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
}

public static class MaxioServiceCollectionExtensions
{
    private const string ClientName = "Maxio";

    /// <summary>
    /// Validates the Maxio settings (refusing to start when one is missing or blank) and registers the SDK
    /// client as a singleton. The options are built once here, so a rotated key needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it via user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.Environment))
            throw new InvalidOperationException("Maxio:Environment is not configured. Set it via user-secrets or your secret store.");

        ServerEnvironment environment;
        if (settings.Environment.Trim().Equals("US", StringComparison.OrdinalIgnoreCase))
            environment = ServerEnvironment.Us;
        else if (settings.Environment.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase))
            environment = ServerEnvironment.Eu;
        else
            throw new InvalidOperationException("Maxio:Environment must be 'US' or 'EU'.");

        services.AddHttpClient(ClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                // The app owns write retries: only GETs are ever resent by the SDK.
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    HttpMethodsToRetry = new[] { System.Net.Http.HttpMethod.Get },
                },
            };
            options.Logging = new LoggingOptions
            {
                // Explicit factory: MAXIO_ADVANCED_BILLING_LOG can never force body logging on.
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                LogRequestBody = false,
            };
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Optional override, used verbatim.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(ClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
