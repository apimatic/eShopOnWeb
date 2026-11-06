using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Builds the Maxio SDK client from the "Maxio:" settings the first time it is needed,
/// so the rest of the API keeps working when billing is not configured.
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
                return _client ??= Create();
            }
        }
    }

    private MaxioAdvancedBillingClient Create()
    {
        var s = _settings.Value;
        if (string.IsNullOrWhiteSpace(s.ApiKey) || string.IsNullOrWhiteSpace(s.Subdomain))
        {
            throw new BillingException(503, "Billing is not configured (Maxio:ApiKey and Maxio:Subdomain are required).");
        }

        var environment = ServerEnvironment.Us;
        if (!string.IsNullOrWhiteSpace(s.Environment))
        {
            if (string.Equals(s.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Us;
            else if (string.Equals(s.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase)) environment = ServerEnvironment.Eu;
            else throw new BillingException(503, "Billing is misconfigured (Maxio:Environment must be US or EU).");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            // Maxio takes the API key as the basic-auth user name; the password is ignored.
            BasicAuth = new BasicAuthCredentials { Username = s.ApiKey!, Password = "x" },
        };
        options.Server.Production.Us.Site = s.Subdomain!;
        options.Server.Production.Eu.Site = s.Subdomain!;
        if (!string.IsNullOrWhiteSpace(s.BaseUrl))
        {
            // Maxio:BaseUrl is used verbatim as the API's base address.
            options.Server.Production.Us.BaseUrl = s.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = s.BaseUrl!;
        }

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(), options);
    }
}
