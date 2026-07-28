using System;
using System.Collections.Generic;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// DI wiring for the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>The named <see cref="IHttpClientFactory"/> client the Maxio SDK client is built over.</summary>
    private const string HttpClientName = "Maxio";

    /// <summary>
    /// Binds <see cref="MaxioSettings"/> from the <c>Maxio</c> configuration section, registers a
    /// long-lived <see cref="MaxioAdvancedBillingClient"/> over a dedicated <see cref="IHttpClientFactory"/>
    /// client, and registers <see cref="IMaxioBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // A dedicated named HttpClient scopes the primary handler to this SDK (no app-wide blast radius).
        // PooledConnectionLifetime forces periodic connection (and DNS) refresh: the SDK's own DI extension
        // registers the client as a singleton over a single CreateClient(), so IHttpClientFactory handler
        // rotation never reaches it — this is the supported mitigation per dotnet-client-initialization.
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        // The SDK client is meant to be long-lived; register it as a singleton over the shared, factory-managed
        // HttpClient. Configuration is validated lazily here (on first resolution) rather than at startup so
        // this additive billing feature never blocks the rest of the API from booting when it is unconfigured —
        // a subscription call then fails clearly, while catalog/auth keep working.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            ValidateSettings(opts);

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = opts.ApiKey, Password = "x" },
            };
            clientOptions.Server.Production.Us.Site = opts.Subdomain;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = opts.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }

    /// <summary>
    /// Throws a <see cref="BillingException"/> (503) naming any missing required <c>Maxio:*</c> settings.
    /// Invoked when the SDK client is first resolved, i.e. on the first subscription call.
    /// </summary>
    private static void ValidateSettings(MaxioSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            missing.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}");
        }
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            missing.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}");
        }
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            missing.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}");
        }
        if (missing.Count > 0)
        {
            throw new BillingException(
                $"Maxio billing is not configured. Missing required setting(s): {string.Join(", ", missing)}.",
                503);
        }
    }
}
