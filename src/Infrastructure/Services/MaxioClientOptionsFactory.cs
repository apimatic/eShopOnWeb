using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Builds the Maxio SDK client options from <see cref="MaxioSettings"/>. This is the one place the
/// outbound target server and the credentials are turned into SDK options, so the runtime client and
/// the operator seeding tool can never disagree about which server they are talking to.
/// </summary>
public static class MaxioClientOptionsFactory
{
    /// <summary>The fixed Basic-auth password the provider expects alongside the API key.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>
    /// Creates SDK options whose base URL is the explicit <c>Maxio:BaseUrl</c> when configured, and
    /// otherwise the host derived from the subdomain and region. The site is always set alongside the
    /// base URL so a template-bearing custom URL resolves as well.
    /// </summary>
    /// <exception cref="BillingConfigurationException">No API key is configured.</exception>
    public static MaxioAdvancedBillingClientOptions Create(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingConfigurationException(
                "No Maxio API key is configured. Set 'Maxio:ApiKey' in user-secrets or the environment.");
        }

        var baseUrl = settings.ResolveBaseUrl();
        var site = settings.Subdomain?.Trim() ?? string.Empty;

        var options = new MaxioAdvancedBillingClientOptions
        {
            // The API key is the Basic-auth username; the password is a fixed placeholder.
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = ApiKeyPasswordPlaceholder
            }
        };

        if (settings.IsEuropeanRegion)
        {
            options.Environment = ServerEnvironment.Eu;
            options.Server.Production.Eu.BaseUrl = baseUrl;
            options.Server.Production.Eu.Site = site;
        }
        else
        {
            options.Environment = ServerEnvironment.Us;
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Us.Site = site;
        }

        return options;
    }
}
