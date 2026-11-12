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

public static class MaxioServiceCollectionExtensions
{
    private const string CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio client from the "Maxio:" configuration section. Throws (so the host refuses to
    /// start) when a required setting is missing or blank. Options are built once; a rotated key needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it with user-secrets or the environment.");
        var baseUrlOverride = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && baseUrlOverride is null)
            throw new InvalidOperationException("Maxio:Subdomain is not configured (and Maxio:BaseUrl is not set).");

        var useEu = (settings.Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "eu" => true,
            "" or "us" or "sandbox" or "production" or "live" => false,
            _ => throw new InvalidOperationException("Maxio:Environment must be US or EU.")
        };

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = useEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            // The SDK only resends GET/HEAD; writes are never resent (their outcome is reconciled by the app).
            Retry = RetryOptions.Default() with
            {
                HttpMethodsToRetry = [HttpMethod.Get, HttpMethod.Head],
                Timeout = TimeSpan.FromSeconds(15)
            }
        };
        var site = settings.Subdomain ?? string.Empty;
        if (useEu)
        {
            options.Server.Production.Eu.Site = site;
            if (baseUrlOverride is not null) options.Server.Production.Eu.BaseUrl = baseUrlOverride;
        }
        else
        {
            options.Server.Production.Us.Site = site;
            if (baseUrlOverride is not null) options.Server.Production.Us.BaseUrl = baseUrlOverride;
        }

        services.AddHttpClient(CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Request bodies carry customer data: LogRequestBody stays off, and the factory is assigned
            // explicitly so the SDK's log environment variable cannot switch body logging on.
            options.Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() };
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });
        services.AddSingleton<MaxioBillingService>();
        services.AddScoped<OperationClaimStore>();
        return services;
    }
}
