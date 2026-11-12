using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings read from the "Maxio" configuration section. None of the values are hard-coded; the
/// sandbox values are supplied through .NET user-secrets (or any other configuration source).
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio:ApiKey</summary>
    public string? ApiKey { get; set; }
    /// <summary>Maxio:Subdomain - the Maxio site (the sandbox site for non-production deployments).</summary>
    public string? Subdomain { get; set; }
    /// <summary>Maxio:Environment - "us" (also "sandbox"/"production") or "eu".</summary>
    public string? Environment { get; set; }
    /// <summary>Maxio:BaseUrl - optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public ServerEnvironment ResolveEnvironment() =>
        (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "us" or "sandbox" or "production" => ServerEnvironment.Us,
            "eu" => ServerEnvironment.Eu,
            _ => throw new InvalidOperationException("Maxio:Environment must be one of: us, sandbox, production, eu.")
        };

    public static string? Validate(MaxioSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            return "Maxio:ApiKey is not configured. Set it with user-secrets or another configuration source.";
        var hasBaseUrl = !string.IsNullOrWhiteSpace(s.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(s.Subdomain))
            return "Maxio:Subdomain is not configured (it is required unless Maxio:BaseUrl is set).";
        if (string.IsNullOrWhiteSpace(s.Environment))
            return "Maxio:Environment is not configured.";
        try { s.ResolveEnvironment(); }
        catch (InvalidOperationException ex) { return ex.Message; }
        if (hasBaseUrl && !(Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)))
            return "Maxio:BaseUrl must be an absolute http(s) URL.";
        return null;
    }
}

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => MaxioSettings.Validate(s) is null, "Maxio settings are invalid.")
            .ValidateOnStart();

        // Fail fast with a message that names the missing key (never its value).
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        var problem = MaxioSettings.Validate(settings);
        if (problem is not null)
            throw new InvalidOperationException(problem);

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Built once at registration and captured by the singleton: a rotated key needs a restart.
        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var environment = s.ResolveEnvironment();
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials { Username = s.ApiKey!, Password = "x" },
                // Writes are sent once: the SDK must not resend PUTs (switch plan, next billing date, restart).
                Retry = RetryOptions.Default() with
                {
                    HttpMethodsToRetry = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Options],
                    Timeout = TimeSpan.FromSeconds(8)
                },
                // Customer names and emails travel in bodies: never log bodies, and never let the
                // MAXIO_ADVANCED_BILLING_LOG variable arm the SDK's own logger.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            if (environment == ServerEnvironment.Eu)
            {
                if (!string.IsNullOrWhiteSpace(s.BaseUrl)) options.Server.Production.Eu.BaseUrl = s.BaseUrl!;
                else options.Server.Production.Eu.Site = s.Subdomain!;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(s.BaseUrl)) options.Server.Production.Us.BaseUrl = s.BaseUrl!;
                else options.Server.Production.Us.Site = s.Subdomain!;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
