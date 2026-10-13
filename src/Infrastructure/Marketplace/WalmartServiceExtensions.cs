using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;
using WalmartApIs.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace;

public static class WalmartServiceExtensions
{
    /// <summary>
    /// Registers the Walmart Marketplace inventory integration: binds <c>Walmart:</c> settings with
    /// start-up validation, constructs the Walmart APIs SDK client (WalletAuth client credentials,
    /// optional base-URL override), and registers <see cref="IWalmartInventoryService"/>.
    /// </summary>
    public static IServiceCollection AddWalmartInventory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WalmartSettings>()
            .Bind(configuration.GetSection(WalmartSettings.ConfigSection));

        var settings = configuration.GetSection(WalmartSettings.ConfigSection).Get<WalmartSettings>() ?? new WalmartSettings();

        // Fail fast: refuse to start if either credential is missing or blank — rather than booting
        // degraded and discovering it as a 401 on the first call in production. Runs during service
        // registration (before the host is ready). Both parts are checked (a blank part is not a
        // missing one), and the message names the config key without ever echoing a value.
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new System.InvalidOperationException(
                "Walmart:ClientId is not configured. Set it via WALMART_CLIENT_ID, user-secrets, or your secret store before starting the app.");
        }
        if (string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new System.InvalidOperationException(
                "Walmart:ClientSecret is not configured. Set it via WALMART_CLIENT_SECRET, user-secrets, or your secret store before starting the app.");
        }

        services.AddWalmartApIsClient(options =>
        {
            options.Environment = ServerEnvironment.Production;

            // Inventory operations authenticate with WalletAuth (OAuth2 client credentials); the
            // token endpoint is derived from the Default1 server group, so the base-URL override
            // below redirects token acquisition as well as the API calls.
            options.WalletAuth = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            // Bound each attempt explicitly; the whole-call budget lives in WalmartInventoryService.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                ApplyBaseUrlOverride(options, settings.BaseUrl!);
            }
        });

        services.AddScoped<IWalmartInventoryService, WalmartInventoryService>();
        return services;
    }

    /// <summary>
    /// Replaces the scheme/host/port of every Walmart server URL with those of
    /// <paramref name="baseUrl"/>, keeping the contract's path intact. Applied to both server groups
    /// so API calls and WalletAuth token acquisition alike go to the override.
    /// </summary>
    private static void ApplyBaseUrlOverride(WalmartApIsClientOptions options, string baseUrl)
    {
        var overrideUri = new Uri(baseUrl, UriKind.Absolute);
        options.Server.Default.Production.BaseUrl = Rebase(options.Server.Default.Production.BaseUrl, overrideUri);
        options.Server.Default1.Production.BaseUrl = Rebase(options.Server.Default1.Production.BaseUrl, overrideUri);
    }

    private static string Rebase(string originalUrl, Uri overrideUri)
    {
        var original = new Uri(originalUrl, UriKind.Absolute);
        // Keep everything after the host (the contract's base path); swap scheme/host/port only.
        return $"{overrideUri.Scheme}://{overrideUri.Authority}{original.AbsolutePath}".TrimEnd('/');
    }
}
