using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient Create(IConfiguration configuration, HttpClient httpClient)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Maxio is not configured: 'Maxio:ApiKey' is missing.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Maxio is not configured: 'Maxio:Subdomain' is missing.");

        var environment = ServerEnvironment.Default();
        if (!string.IsNullOrWhiteSpace(settings.Environment))
        {
            var name = settings.Environment.Trim();
            if (string.Equals(name, "EU", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Eu;
            else if (string.Equals(name, "US", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Us;
            else throw new BillingException(HttpStatusCode.ServiceUnavailable, "Maxio is misconfigured: 'Maxio:Environment' must be US or EU.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            // Maxio authenticates API keys as the basic-auth user name with any password.
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
        };
        options.Server.Production.Us.Site = settings.Subdomain!;
        options.Server.Production.Eu.Site = settings.Subdomain!;

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Verbatim override: used as the API's base address instead of one derived from the subdomain.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
