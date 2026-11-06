using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    public static MaxioAdvancedBillingClient Create(MaxioOptions settings, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new BillingException(BillingFailureKind.Unavailable, "Billing is not configured (Maxio:ApiKey).");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new BillingException(BillingFailureKind.Unavailable, "Billing is not configured (Maxio:Subdomain).");

        var eu = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = eu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            // Retries: status retries only touch idempotent verbs, but transport failures are resent on every
            // verb (the SDK cannot disable that), so writes are reconciled in SubscriptionService.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(30) }
        };

        // Both environments are configured; only the selected one is read.
        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            options.Server.Production.Eu.Site = settings.Subdomain;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Verbatim override of the API base address.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
