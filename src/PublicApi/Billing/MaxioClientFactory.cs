using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Builds the Maxio SDK client lazily from the "Maxio:" settings, so the app still starts
/// (and unrelated endpoints work) when billing is not configured.
/// </summary>
public class MaxioClientFactory
{
    private readonly IOptions<MaxioSettings> _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _lock = new();
    private MaxioAdvancedBillingClient? _client;

    public MaxioClientFactory(IOptions<MaxioSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
    }

    public MaxioAdvancedBillingClient GetClient()
    {
        lock (_lock)
        {
            return _client ??= Create();
        }
    }

    private MaxioAdvancedBillingClient Create()
    {
        var s = _settings.Value;
        if (string.IsNullOrWhiteSpace(s.ApiKey))
        {
            throw new BillingException(503, "Billing is not configured: Maxio:ApiKey is missing.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(s.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(s.Subdomain))
        {
            throw new BillingException(503, "Billing is not configured: set Maxio:Subdomain (or Maxio:BaseUrl).");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = s.ApiKey!, Password = "x" },
            Environment = ParseEnvironment(s.Environment),
        };

        if (hasBaseUrl)
        {
            // Used as the API's base address instead of deriving one from the subdomain.
            var baseUrl = s.BaseUrl!.Trim().TrimEnd('/');
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = s.Subdomain!.Trim();
            options.Server.Production.Eu.Site = s.Subdomain!.Trim();
        }

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(), options);
    }

    private static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ServerEnvironment.Us;
        return value.Trim().ToUpperInvariant() switch
        {
            "US" => ServerEnvironment.Us,
            "EU" => ServerEnvironment.Eu,
            _ => throw new BillingException(503, "Billing is not configured: Maxio:Environment must be US or EU."),
        };
    }
}
