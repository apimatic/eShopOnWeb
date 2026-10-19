using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static MaxioAdvancedBillingClientOptions CreateOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout
            },
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        else
            options.Server.Production.Us.Site = settings.Subdomain;

        return options;
    }
}
