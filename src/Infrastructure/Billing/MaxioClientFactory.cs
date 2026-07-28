using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Builds the single, long-lived <see cref="MaxioAdvancedBillingClient"/> from <see cref="MaxioSettings"/>.
/// Construction never performs I/O and never throws on missing configuration, so it is safe to resolve
/// during DI validation / app startup; misconfiguration is reported lazily by
/// <see cref="MaxioBillingService"/> when an operation is actually attempted.
/// </summary>
internal static class MaxioClientFactory
{
    // One handler for the process lifetime. PooledConnectionLifetime bounds how long a pooled
    // connection is reused, so DNS changes are eventually honoured even though the client is a singleton
    // (the SDK client is meant to be long-lived and does not own the HttpClient).
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    public static MaxioAdvancedBillingClient Create(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey ?? string.Empty, Password = "x" }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Explicit override: use the configured base URL verbatim.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            // Derive the base URL from the site subdomain.
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        var httpClient = new HttpClient(SharedHandler, disposeHandler: false);
        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
