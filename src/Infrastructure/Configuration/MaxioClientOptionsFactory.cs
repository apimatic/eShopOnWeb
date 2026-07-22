using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Builds the provider SDK's client options from eShopOnWeb's typed settings. This is the one place
/// the outbound target server is decided, so pointing the same build at production, a dev/sandbox
/// tenant, or a local mock is a configuration change and never a code change.
/// </summary>
public static class MaxioClientOptionsFactory
{
    /// <summary>
    /// Produces client options whose base address is <see cref="MaxioSettings.ResolveBaseUrl"/> — an
    /// explicit <c>Maxio:BaseUrl</c> verbatim when configured, otherwise the host derived from the
    /// subdomain and region. The resolved URL is assigned directly rather than left as a template, so
    /// an explicit override can never be silently ignored.
    /// </summary>
    public static MaxioAdvancedBillingClientOptions Create(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        var baseUrl = settings.ResolveBaseUrl();

        if (settings.IsEuropeanRegion)
        {
            options.Server.Production.Eu.Site = settings.Subdomain;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }
}
