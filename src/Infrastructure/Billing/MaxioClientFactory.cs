using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    /// <summary>Builds the SDK client from settings. Throws InvalidOperationException ("Maxio:...") when settings are incomplete.</summary>
    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) is not configured.");

        var isEu = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
        };

        var baseUrl = hasBaseUrl ? settings.BaseUrl!.Trim() : null;
        var site = settings.Subdomain?.Trim();
        if (isEu)
        {
            var eu = options.Server.Production.Eu;
            if (baseUrl != null) eu.BaseUrl = baseUrl; else eu.Site = site!;
        }
        else
        {
            var us = options.Server.Production.Us;
            if (baseUrl != null) us.BaseUrl = baseUrl; else us.Site = site!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
