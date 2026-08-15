using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio billing integration: binds <see cref="MaxioSettings"/> from the <c>Maxio:</c>
/// configuration section, constructs a long-lived <see cref="MaxioAdvancedBillingClient"/>, and wires
/// <see cref="IMaxioBillingService"/>.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION));

        // The SDK client is long-lived (lightweight controller wrappers over a shared HTTP pipeline)
        // and owns no HttpClient itself, so we register it as a singleton over a single HttpClient.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            return CreateClient(settings);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        return services;
    }

    private static MaxioAdvancedBillingClient CreateClient(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException($"{MaxioSettings.CONFIG_SECTION}:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException(
                $"Either {MaxioSettings.CONFIG_SECTION}:BaseUrl or {MaxioSettings.CONFIG_SECTION}:Subdomain must be configured.");

        var environment = ParseEnvironment(settings.Environment);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            // HTTP Basic: username = API key, password = literal "x" (Maxio convention).
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" }
        };

        // Explicit BaseUrl override wins and is used verbatim; otherwise the base URL is derived from
        // the site subdomain. Server options are read for the selected environment only.
        if (environment == ServerEnvironment.Eu)
        {
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            else
                options.Server.Production.Eu.Site = settings.Subdomain;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            else
                options.Server.Production.Us.Site = settings.Subdomain;
        }

        // The SDK does not own the HttpClient; keep one long-lived instance with a bounded pooled
        // connection lifetime so a singleton client still picks up DNS changes over time.
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    // ServerEnvironment is an environment selector that does not expose a public FromValue, so map the
    // configured string to a constant ourselves, defaulting to US.
    private static ServerEnvironment ParseEnvironment(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "EU" => ServerEnvironment.Eu,
            _ => ServerEnvironment.Us
        };
}
