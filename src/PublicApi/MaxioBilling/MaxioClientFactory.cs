using System;
using System.Net.Http;
using System.Threading;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

/// <summary>
/// Builds the Maxio Advanced Billing SDK client from <see cref="MaxioOptions"/>.
/// </summary>
public static class MaxioClientFactory
{
    /// <summary>
    /// Per-attempt timeout for calls to Maxio (both the SDK retry pipeline and
    /// the HttpClient backstop) so a hung provider cannot pin a request for
    /// the 100s default.
    /// </summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    public static MaxioAdvancedBillingClient Create(MaxioOptions options, HttpClient httpClient)
    {
        var isEu = string.Equals(options.Environment?.Trim(), "eu", StringComparison.OrdinalIgnoreCase);

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey,
                Password = "x"
            },
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                Timeout = PerAttemptTimeout
            }
        };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            // Explicit base address override — used verbatim.
            if (isEu)
            {
                clientOptions.Server.Production.Eu.BaseUrl = options.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }
        }
        else if (isEu)
        {
            clientOptions.Server.Production.Eu.Site = options.Subdomain;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = options.Subdomain;
        }

        return new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }
}
