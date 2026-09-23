using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the subscription-billing capability: Maxio settings (fail-fast), the Maxio SDK client over a
/// dedicated named <see cref="System.Net.Http.HttpClient"/>, the local billing store, and the service.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.SectionName).Bind(settings);

        // Fail-fast: refuse to start rather than surfacing a missing secret as a 401 on the first call.
        // Each part is checked independently — a blank part is misconfigured, not merely absent — and the
        // message names the config key without ever echoing a value.
        RequireSetting(settings.ApiKey, $"{MaxioSettings.SectionName}:ApiKey", "MAXIO_API_KEY");
        RequireSetting(settings.Subdomain, $"{MaxioSettings.SectionName}:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        RequireSetting(settings.ProductFamilyHandle, $"{MaxioSettings.SectionName}:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        // BaseUrl is an optional override; if the key is present it must not be blank.
        if (configuration.GetSection($"{MaxioSettings.SectionName}:BaseUrl").Exists() &&
            string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException(
                $"{MaxioSettings.SectionName}:BaseUrl is present but blank. Remove it to derive the URL from " +
                $"{MaxioSettings.SectionName}:Subdomain, or set a non-blank value.");
        }

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // Local billing store, mirroring the host's in-memory-vs-SqlServer choice.
        bool useOnlyInMemoryDatabase = false;
        if (configuration["UseOnlyInMemoryDatabase"] != null)
        {
            useOnlyInMemoryDatabase = bool.Parse(configuration["UseOnlyInMemoryDatabase"]!);
        }

        if (useOnlyInMemoryDatabase)
        {
            services.AddDbContext<MaxioBillingContext>(c => c.UseInMemoryDatabase("MaxioBilling"));
        }
        else
        {
            services.AddDbContext<MaxioBillingContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));
        }

        // A dedicated named HttpClient keeps this SDK's timeout/handler off the shared default client.
        // PooledConnectionLifetime keeps DNS fresh behind the long-lived singleton client; the HttpClient
        // timeout is a per-attempt backstop above the SDK's own per-attempt Retry.Timeout.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioSettings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // Options are built once here and captured in the singleton: a rotated API key takes effect on
            // the next process restart.
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioSettings.ApiKey!,
                    Password = "x" // Chargify Basic auth: API key as username, literal "x" as password.
                },
                // Per-attempt timeout (POST is not resent by the SDK; the whole-call budget lives in the service).
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
                // Assign the logger factory explicitly so MAXIOADVANCEDBILLINGCLIENT_LOG cannot switch on
                // request-body logging from the environment; bodies are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            if (!string.IsNullOrWhiteSpace(maxioSettings.BaseUrl))
            {
                // Optional override: used verbatim as the base address.
                options.Server.Production.Us.BaseUrl = maxioSettings.BaseUrl!;
            }
            else
            {
                // Derive https://{site}.chargify.com from the sandbox/production subdomain.
                options.Server.Production.Us.Site = maxioSettings.Subdomain!;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void RequireSetting(string? value, string key, string envVarName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it (env {envVarName}) via user-secrets, environment variable, " +
                "or your secret store before starting the app.");
        }
    }
}
