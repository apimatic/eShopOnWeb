using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Builds the Maxio SDK client from the <c>Maxio:</c> settings. The client is created on first use so the
/// app can start (and serve the rest of the API) even when billing is not configured.
/// </summary>
public class MaxioClientProvider
{
    private readonly IOptions<MaxioSettings> _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _lock = new();
    private MaxioAdvancedBillingClient? _client;

    public MaxioClientProvider(IOptions<MaxioSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
    }

    public MaxioAdvancedBillingClient Client
    {
        get
        {
            lock (_lock)
            {
                return _client ??= Create();
            }
        }
    }

    private MaxioAdvancedBillingClient Create()
    {
        var settings = _settings.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingException(503, "Billing is not configured: Maxio:ApiKey is missing.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
        };

        if (!string.IsNullOrWhiteSpace(settings.Environment))
        {
            if (string.Equals(settings.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
            {
                options.Environment = ServerEnvironment.Eu;
            }
            else if (string.Equals(settings.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase))
            {
                options.Environment = ServerEnvironment.Us;
            }
            else
            {
                throw new BillingException(503, "Billing is not configured: Maxio:Environment must be US or EU.");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Explicit override: used as the API's base address, ignoring the subdomain.
            var baseUrl = settings.BaseUrl!.Trim().TrimEnd('/');
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new BillingException(503, "Billing is not configured: Maxio:Subdomain is missing.");
            }
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain.Trim();
        }

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(), options);
    }
}
