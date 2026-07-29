using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the SDK client options from <see cref="MaxioSettings"/>. Kept separate from DI wiring so the
/// exact same option shape can be constructed in tests. Auth is HTTP Basic where the username is the
/// Maxio API key and the password is the literal "x".
/// </summary>
public static class MaxioClientOptionsFactory
{
    public static MaxioAdvancedBillingClientOptions Create(MaxioSettings settings)
    {
        // Sandbox and production Maxio sites are hosted on the US environment; an explicit BaseUrl
        // override (e.g. a mock host) can still redirect traffic regardless of environment.
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Explicit override wins and is used verbatim.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else
        {
            // Derive the base address from the site subdomain.
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        return options;
    }
}
