using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the "Maxio:" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Throws when the settings cannot produce a working client, so the host fails at start-up.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Configuration value 'Maxio:ApiKey' is missing or blank.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Configuration value 'Maxio:Subdomain' is missing or blank (and 'Maxio:BaseUrl' is not set).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Configuration value 'Maxio:BaseUrl' is not an absolute URL.");
        }

        ResolveEnvironment();
    }

    public ServerEnvironment ResolveEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment))
        {
            return ServerEnvironment.Us;
        }

        return Environment.Trim().ToUpperInvariant() switch
        {
            "US" => ServerEnvironment.Us,
            "EU" => ServerEnvironment.Eu,
            _ => throw new InvalidOperationException($"Configuration value 'Maxio:Environment' must be 'US' or 'EU' (was '{Environment}').")
        };
    }
}

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio SDK client (singleton, options captured once at registration) and the billing service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound lazily so configuration added after registration (e.g. by a test host) is honoured.
        // The host refuses to start when the settings are unusable: see MaxioStartupCheck.
        services.AddSingleton(sp =>
        {
            var bound = new MaxioSettings();
            sp.GetRequiredService<IConfiguration>().GetSection(MaxioSettings.CONFIG_NAME).Bind(bound);
            bound.Validate();
            return bound;
        });
        services.AddHostedService<MaxioStartupCheck>();

        const string clientName = "MaxioAdvancedBilling";
        services.AddHttpClient(clientName, c =>
            {
                // Bounds one attempt; the SDK retries idempotent verbs only.
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(clientName);
            var environment = settings.ResolveEnvironment();
            var options = new MaxioAdvancedBillingClientOptions
            {
                // The API key is the Basic-auth user name; the password is the literal "x".
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Environment = environment,
                // Explicit factory: bodies are never logged (they carry customer e-mail addresses).
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override: no template placeholders, so the site subdomain is not applied.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
                options.Server.Production.Eu.Site = settings.Subdomain!;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<MaxioBilling>();
        return services;
    }
}

/// <summary>Resolves the settings and the client at start-up so a missing credential stops the host immediately.</summary>
internal sealed class MaxioStartupCheck : Microsoft.Extensions.Hosting.IHostedService
{
    public MaxioStartupCheck(MaxioSettings settings, MaxioAdvancedBillingClient client)
    {
    }

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
