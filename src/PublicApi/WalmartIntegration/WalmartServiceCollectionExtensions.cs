using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;
using WalmartApIs.Servers;

namespace Microsoft.eShopWeb.PublicApi.WalmartIntegration;

public static class WalmartServiceCollectionExtensions
{
    // Per-attempt SDK timeout. The whole-call budget is enforced separately by a CancellationToken in
    // WalmartInventoryService (see SingleCallBudget / SyncBudget there).
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the Walmart Marketplace integration: binds and validates the <c>Walmart:</c> settings
    /// (the host refuses to start when ClientId or ClientSecret is missing/blank), constructs the
    /// Walmart SDK client with WalletAuth client-credentials and any BaseUrl override, and registers
    /// <see cref="IWalmartInventoryService"/>.
    /// </summary>
    public static IServiceCollection AddWalmartInventory(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: validate both credential parts at startup, not on the first call in production.
        services.AddOptions<WalmartConfiguration>()
            .Bind(configuration.GetSection(WalmartConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read the settings once at registration; the SDK options are captured in the singleton client,
        // so a rotated secret takes effect only on process restart.
        var settings = configuration.GetSection(WalmartConfiguration.SectionName).Get<WalmartConfiguration>()
                       ?? new WalmartConfiguration();

        services.AddWalmartApIsClient(options =>
        {
            // Inventory operations and the WalletAuth token both resolve through the Default1 server
            // group, so the credentials go on WalletAuth.
            options.WalletAuth = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
            };

            options.Environment = ServerEnvironment.Production;

            // Bound each attempt; the whole-call deadline is a CancellationToken in the service.
            options.Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout };

            // Inventory requests carry only sku/quantity (no secret, no PII), but keep request-body
            // logging off as a matter of posture — the client secret is a credential, never a body.
            options.Logging = options.Logging with { LogRequestBody = false };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Replace scheme/host/port of every Walmart call (operations AND token) while keeping the
                // contract's base path. Both server groups resolve through options.Server.
                options.Server.Default.Production.BaseUrl =
                    Rebase(options.Server.Default.Production.BaseUrl, settings.BaseUrl!);
                options.Server.Default1.Production.BaseUrl =
                    Rebase(options.Server.Default1.Production.BaseUrl, settings.BaseUrl!);
            }
        });

        services.AddScoped<IWalmartInventoryService, WalmartInventoryService>();

        return services;
    }

    /// <summary>
    /// Returns <paramref name="contractUrl"/> with its scheme, host and port replaced by those of
    /// <paramref name="overrideBase"/>, preserving the contract's path and query exactly.
    /// </summary>
    public static string Rebase(string contractUrl, string overrideBase)
    {
        var contract = new Uri(contractUrl, UriKind.Absolute);
        var over = new Uri(overrideBase, UriKind.Absolute);

        var builder = new UriBuilder
        {
            Scheme = over.Scheme,
            Host = over.Host,
            Port = over.IsDefaultPort ? -1 : over.Port,
            Path = contract.AbsolutePath,
            Query = contract.Query,
        };
        return builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }
}
