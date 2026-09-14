using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClientOptions CreateOptions(MaxioOptions configured)
    {
        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(8)
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = configured.ApiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrWhiteSpace(configured.BaseUrl))
        {
            sdkOptions.Server.Production.Us.BaseUrl = configured.BaseUrl;
        }
        else
        {
            sdkOptions.Server.Production.Us.Site = configured.Subdomain;
        }

        return sdkOptions;
    }
}
