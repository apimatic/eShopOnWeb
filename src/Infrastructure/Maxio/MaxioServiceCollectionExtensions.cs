using System;
using System.Net.Http;
using Maxio;
using Maxio.Core.Authentication.Basic;
using Maxio.Core.Configuration;
using Maxio.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers Maxio subscription billing: binds and validates <see cref="MaxioSettings"/> at startup
    /// (the host refuses to boot on a missing/blank credential), builds the <see cref="MaxioClient"/> once
    /// over a dedicated named <see cref="HttpClient"/>, and registers
    /// <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        // Fail-fast: validate at startup (this runs during host build, before any request) rather than
        // discovering a blank credential as a first-call 401. Every required key is checked; a blank part is
        // not a missing one, so each is checked independently. The message names the key, never the value.
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();
        ValidateOrThrow(settings);

        // A dedicated, long-lived HttpClient pipeline for the SDK — keeps its timeout/handler off the shared
        // default client, and recycles pooled connections so a singleton MaxioClient never pins stale DNS.
        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt bound (the SDK's own Timeout is also per attempt). The whole-call budget is a
                // CancellationToken deadline enforced in MaxioBillingService.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Build the SDK options ONCE and capture them in the singleton client (a rotated key needs a restart).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioClientOptions
            {
                Environment = ServerEnvironment.Us,
                // Basic auth: username = Chargify API key, password = literal "x".
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                // Assign LoggerFactory explicitly so request logging goes through the host's pipeline AND the
                // MAXIOCLIENT_LOG environment variable can never arm unredacted body logging from outside the code.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Base URL: use Maxio:BaseUrl verbatim when supplied, else derive https://{Subdomain}.chargify.com
            // from the site subdomain. Configure server options BEFORE constructing the client.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            return new MaxioClient(httpClient, options);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private static void ValidateOrThrow(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or environment before starting the app.");
        }
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain (or Maxio:BaseUrl) is not configured. Set one via user-secrets or environment before starting the app.");
        }
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or environment before starting the app.");
        }
    }
}
