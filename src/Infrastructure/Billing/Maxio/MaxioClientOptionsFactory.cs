using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Turns <see cref="MaxioSettings"/> into the SDK's options object.
/// </summary>
internal static class MaxioClientOptionsFactory
{
    /// <summary>
    /// Maxio's Basic-auth scheme carries the API key as the user name and a fixed literal as the password.
    /// This is a protocol constant, not a credential.
    /// </summary>
    private const string ApiKeyBasicAuthPassword = "x";

    public static MaxioAdvancedBillingClientOptions Create(MaxioSettings settings)
    {
        var environment = ResolveEnvironment(settings.Environment);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = ApiKeyBasicAuthPassword
            },
            Retry = RetryOptions.Default() with
            {
                // Polly rejects zero attempts, so 1 is the floor. Writes are additionally protected against
                // transport-triggered resends by MaxioCallScopeHandler.
                MaxRetries = Math.Max(1, settings.MaxRetries),
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds))
            }
        };

        // `Environment` decides WHICH branch of Server.Production is read, so the overrides have to be
        // written to that same branch — writing them to the other one is silently ignored.
        if (environment == ServerEnvironment.Eu)
        {
            Apply(settings, url => options.Server.Production.Eu.BaseUrl = url, site => options.Server.Production.Eu.Site = site);
        }
        else
        {
            Apply(settings, url => options.Server.Production.Us.BaseUrl = url, site => options.Server.Production.Us.Site = site);
        }

        return options;
    }

    private static void Apply(MaxioSettings settings, Action<string> setBaseUrl, Action<string> setSite)
    {
        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            // The default BaseUrl template is "https://{site}.chargify.com"; the SDK substitutes {site}.
            setSite(settings.Subdomain!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim: the configured value carries no {site} token, so the SDK's substitution is a
            // no-op and the address is exactly what was configured.
            setBaseUrl(settings.BaseUrl!.Trim());
        }
    }

    /// <summary>
    /// The SDK models exactly two hosting regions and offers no way to construct a third, so anything that
    /// is not an explicit EU selection resolves to US. Sandbox targeting is done with the site subdomain.
    /// </summary>
    private static ServerEnvironment ResolveEnvironment(string? configured) =>
        string.Equals(configured?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;
}
