using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK runs on.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the <c>Maxio</c>
    /// configuration section.
    /// </summary>
    /// <remarks>
    /// The SDK ships its own <c>AddMaxioAdvancedBillingClient</c> helper, but it resolves the default, unnamed
    /// <see cref="IHttpClientFactory"/> client - so the timeout and the message handler this integration needs
    /// would apply to every other unnamed <c>CreateClient()</c> consumer in the app. Registering over a named
    /// client keeps that blast radius to this SDK, and lets the primary handler set
    /// <c>PooledConnectionLifetime</c>, which a long-lived singleton client otherwise never gets.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddTransient<MaxioHttpDiagnosticsHandler>();

        // Settings are read when the client is first resolved rather than here, so this registration can sit
        // anywhere in Program.cs without depending on which configuration sources have been added yet.
        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                // Bounds one attempt, not the whole call: the retry pipeline sits above SendAsync, so every
                // attempt gets a fresh window. Its value is that the first expiry ends the call - the cheapest
                // guard against a hung provider pinning a request thread.
                client.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds);
            })
            .AddHttpMessageHandler<MaxioHttpDiagnosticsHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient for the process lifetime, so the
                // factory handler rotation never reaches it. Without this, a DNS change is cached forever.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<MaxioClientAccessor>>();
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var problems = settings.Validate();

            if (problems.Count > 0)
            {
                logger.LogWarning(
                    "Maxio subscription billing is disabled because its configuration is incomplete: {Problems} " +
                    "The subscription endpoints will report 503 until this is fixed.",
                    string.Join(" ", problems));

                return new MaxioClientAccessor(problems);
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var client = new MaxioAdvancedBillingClient(httpClient, BuildOptions(settings));

            logger.LogInformation(
                "Maxio subscription billing enabled for product family '{ProductFamily}' ({Target}).",
                settings.ProductFamilyHandle,
                string.IsNullOrWhiteSpace(settings.BaseUrl) ? $"site '{settings.Subdomain}'" : "custom base URL");

            return new MaxioClientAccessor(client);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>Translates <see cref="MaxioSettings"/> into the SDK's options object.</summary>
    public static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var isEu = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);

        var options = new MaxioAdvancedBillingClientOptions
        {
            // ServerEnvironment is a closed string-enum with only Us and Eu constructible and no public
            // FromValue, so the configuration string is mapped here rather than parsed by the SDK.
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                // One is the floor the retry pipeline accepts; writes are additionally protected from
                // re-sends by MaxioHttpDiagnosticsHandler.
                MaxRetries = Math.Max(1, settings.MaxRetries),
                Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds)
            }
        };

        // Only the branch matching options.Environment is ever read, and Site defaults to the literal string
        // "subdomain" rather than to null - so an unset Site silently targets the wrong host.
        if (isEu)
        {
            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
            }

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            // A base URL containing no {site} token is used verbatim; the subdomain is simply never
            // substituted. That is what makes this an override rather than a second template.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
        }

        return options;
    }
}
