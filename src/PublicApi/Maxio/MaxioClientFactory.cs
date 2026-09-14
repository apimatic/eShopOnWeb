using System;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Builds the Maxio Advanced Billing SDK client from <see cref="MaxioOptions"/>.
/// Shared by DI registration and tests (tests stub the <see cref="HttpClient"/> seam).
/// </summary>
public static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = options.ApiKey, Password = "x" },
            Environment = string.Equals(options.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
            Server = new ServerOptions
            {
                Production = { Us = { Site = options.Subdomain } }
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(10)
            }
        };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }
}
