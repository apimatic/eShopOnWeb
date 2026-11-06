using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Builds the Maxio SDK client lazily from the "Maxio:" settings, so that the API still starts
/// (and serves the rest of the shop) when billing is not configured.
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
            if (_client != null) return _client;
            lock (_lock)
            {
                return _client ??= Build();
            }
        }
    }

    private MaxioAdvancedBillingClient Build()
    {
        var s = _settings.Value;
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            throw new BillingException(503, "Billing is not configured: Maxio:ApiKey is missing.");

        var hasBaseUrl = !string.IsNullOrWhiteSpace(s.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(s.Subdomain))
            throw new BillingException(503, "Billing is not configured: Maxio:Subdomain is missing.");

        var environment = ServerEnvironment.Us;
        if (!string.IsNullOrWhiteSpace(s.Environment))
        {
            var value = s.Environment.Trim();
            if (value.Equals("US", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Us;
            else if (value.Equals("EU", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Eu;
            else throw new BillingException(503, "Billing is not configured: Maxio:Environment must be US or EU.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials { Username = s.ApiKey!, Password = "x" },
        };

        if (!string.IsNullOrWhiteSpace(s.Subdomain))
        {
            options.Server.Production.Us.Site = s.Subdomain!.Trim();
            options.Server.Production.Eu.Site = s.Subdomain!.Trim();
        }

        if (hasBaseUrl)
        {
            // Verbatim override of the API base address.
            options.Server.Production.Us.BaseUrl = s.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = s.BaseUrl!;
        }

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(), options);
    }
}
