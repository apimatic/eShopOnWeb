using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingClientFactory
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    public static MaxioAdvancedBillingClient Create(System.Net.Http.HttpClient httpClient, MaxioOptions settings)
    {
        var isEu = string.Equals(
            Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT"),
            "EU",
            StringComparison.OrdinalIgnoreCase);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout,
                MaxRetries = 1
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        if (isEu)
        {
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
