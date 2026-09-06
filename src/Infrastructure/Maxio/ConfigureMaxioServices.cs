using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing client and the subscription-billing service.
/// </summary>
public static class ConfigureMaxioServices
{
    /// <summary>
    /// Name of the dedicated <see cref="HttpClient"/> this integration uses. The SDK's own DI
    /// extension binds to the shared default (unnamed) client, which would mean the timeout, the
    /// primary handler and the single-send guard configured here also applied to every other
    /// unnamed <c>CreateClient()</c> consumer in the app.
    /// </summary>
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);

        // Read only for the values needed to shape the HTTP pipeline itself. Credentials and catalog
        // are resolved from IOptions when the client is first built, never captured here.
        var pipelineSettings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt, not the whole call, and the default of 100s is an outage
                // rather than a timeout on a request path.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, pipelineSettings.AttemptTimeoutSeconds) + 5);
            })
            .AddHttpMessageHandler(() => new SingleSendGuardHandler())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so IHttpClientFactory's handler rotation never
                // reaches it; without this a DNS change would be cached for the process lifetime.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new MaxioAdvancedBillingClient(httpClient, BuildOptions(settings));
        });

        services.AddSingleton<MaxioProductFamilyResolver>();
        services.AddSingleton<MaxioSiteResolver>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>
    /// Builds the SDK options. Every value comes from configuration; nothing about a particular
    /// Maxio site or catalog is baked in.
    /// </summary>
    internal static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ResolveEnvironment(settings.Environment),
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,

                // Maxio's basic-auth scheme carries the API key as the user name; the password is a
                // fixed placeholder.
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds))
            }
        };

        ApplyServerAddress(options, settings);

        return options;
    }

    /// <summary>
    /// Points the client at the right host. An explicit base URL wins and is used verbatim; otherwise
    /// the host is derived from the site subdomain via the SDK's own URL template.
    /// </summary>
    private static void ApplyServerAddress(MaxioAdvancedBillingClientOptions options, MaxioSettings settings)
    {
        var isEu = options.Environment == ServerEnvironment.Eu;

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            var baseUrl = settings.BaseUrl!.Trim();
            if (isEu)
            {
                options.Server.Production.Eu.BaseUrl = baseUrl;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = baseUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            return;
        }

        var site = settings.Subdomain!.Trim();
        if (isEu)
        {
            options.Server.Production.Eu.Site = site;
        }
        else
        {
            options.Server.Production.Us.Site = site;
        }
    }

    /// <summary>
    /// Maps the configured region onto the SDK's environment constant. Server/environment enums do
    /// not reliably expose a public <c>FromValue</c>, so the mapping is written out and defaults
    /// deliberately to US.
    /// </summary>
    private static ServerEnvironment ResolveEnvironment(string? environment) =>
        string.Equals(environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;
}
