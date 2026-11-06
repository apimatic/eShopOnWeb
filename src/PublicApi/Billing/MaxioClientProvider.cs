using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Builds the Maxio SDK client from the <c>Maxio:</c> configuration section.
/// </summary>
public class MaxioClientProvider
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _lock = new();
    private MaxioAdvancedBillingClient? _client;

    public MaxioClientProvider(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
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
        var settings = _configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new BillingException(503, "Billing is not configured: setting Maxio:ApiKey is missing.");

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new BillingException(503, "Billing is not configured: setting Maxio:Subdomain is missing.");

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Environment = ParseEnvironment(settings.Environment),
        };

        if (hasBaseUrl)
        {
            // Used verbatim as the API's base address.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
        }

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(), options);
    }

    private static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ServerEnvironment.Default();
        if (string.Equals(value.Trim(), "US", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Us;
        if (string.Equals(value.Trim(), "EU", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Eu;
        throw new BillingException(503, $"Billing is not configured: Maxio:Environment '{value}' is not US or EU.");
    }
}
