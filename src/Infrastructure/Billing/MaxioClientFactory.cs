using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    /// <summary>
    /// Builds the SDK client from settings. Throws <see cref="BillingUnavailableException"/> when required
    /// settings are missing, so a misconfigured host still starts and only billing endpoints fail.
    /// </summary>
    public static MaxioAdvancedBillingClient Create(MaxioSettings settings, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new BillingUnavailableException("Billing is not configured: 'Maxio:ApiKey' is missing.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new BillingUnavailableException("Billing is not configured: set 'Maxio:Subdomain' (or 'Maxio:BaseUrl').");

        var options = new MaxioAdvancedBillingClientOptions
        {
            // Maxio authenticates with the API key as the username and "x" as the password.
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
            Environment = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Verbatim override of the API base address.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
