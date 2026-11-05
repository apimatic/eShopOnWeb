using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    /// <summary>Builds the SDK client from settings; throws <see cref="BillingNotConfiguredException"/> when incomplete.</summary>
    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioSettings settings)
    {
        MaxioSettingsValidator.EnsureConfigured(settings);

        var options = new MaxioAdvancedBillingClientOptions
        {
            // The API key is the basic-auth username; the password is a fixed placeholder.
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Environment = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
        }
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Explicit override: used verbatim instead of deriving an address from the subdomain.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}

public static class MaxioSettingsValidator
{
    public static void EnsureConfigured(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new BillingNotConfiguredException("Billing is not configured: 'Maxio:ApiKey' is missing.");
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
            throw new BillingNotConfiguredException("Billing is not configured: 'Maxio:ProductFamilyHandle' is missing.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new BillingNotConfiguredException("Billing is not configured: set 'Maxio:Subdomain' or 'Maxio:BaseUrl'.");
    }
}
