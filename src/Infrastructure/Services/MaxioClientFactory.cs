using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Builds configured Maxio SDK clients. This is the single place credentials are attached and the
/// outbound target server is resolved, so the runtime billing client and the one-off operator
/// tooling can never drift apart or point at different hosts.
/// </summary>
public static class MaxioClientFactory
{
    /// <summary>Maxio's Basic-auth scheme puts the API key in the username and ignores the password.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>
    /// Builds SDK options for a deployment. An explicit <c>Maxio:BaseUrl</c> wins; otherwise the
    /// host is derived from the subdomain and region. The host is never hardcoded.
    /// </summary>
    public static MaxioAdvancedBillingClientOptions CreateOptions(MaxioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingConfigurationException(
                "No Maxio API key is configured. Set Maxio:ApiKey in user-secrets or the environment.");
        }

        var baseUrl = settings.ResolveBaseUrl();

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = ApiKeyPasswordPlaceholder
            }
        };

        // The SDK's default host is a placeholder template, so the resolved URL is written onto
        // the region actually in use. Writing it verbatim is what makes an explicit override work.
        if (settings.IsEuRegion)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }

    /// <summary>
    /// Builds a client over a caller-supplied <see cref="HttpClient"/>. The SDK never owns the
    /// <see cref="HttpClient"/>, so callers pass a pooled or long-lived instance.
    /// </summary>
    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        return new MaxioAdvancedBillingClient(httpClient, CreateOptions(settings));
    }
}
