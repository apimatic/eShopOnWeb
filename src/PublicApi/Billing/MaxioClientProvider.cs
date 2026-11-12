using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface IMaxioClientProvider
{
    MaxioAdvancedBillingClient GetClient();
}

/// <summary>
/// Builds the Maxio SDK client once from the "Maxio:" settings and reuses it (a rotated key needs a restart).
/// Missing settings are reported as a 503 naming the configuration key, never a null-reference.
/// </summary>
public class MaxioClientProvider : IMaxioClientProvider
{
    private readonly MaxioSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _gate = new();
    private MaxioAdvancedBillingClient? _client;

    public MaxioClientProvider(IOptions<MaxioSettings> settings, ILoggerFactory loggerFactory)
    {
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
    }

    public MaxioAdvancedBillingClient GetClient()
    {
        if (_client != null) return _client;
        lock (_gate)
        {
            return _client ??= Build();
        }
    }

    private MaxioAdvancedBillingClient Build()
    {
        Require(_settings.ApiKey, "ApiKey");
        Require(_settings.Subdomain, "Subdomain");
        Require(_settings.Environment, "Environment");

        var environment = _settings.Environment!.Trim().ToUpperInvariant() switch
        {
            "US" => ServerEnvironment.Us,
            "EU" => ServerEnvironment.Eu,
            _ => throw new BillingException(503, $"Billing is misconfigured: {MaxioSettings.SectionName}:Environment must be 'US' or 'EU'.")
        };

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey!.Trim(), Password = "x" },
            Environment = environment,
            // Per-attempt timeout; the SDK only resends GET/PUT, never a POST.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) },
            Logging = new LoggingOptions { LoggerFactory = _loggerFactory },
        };
        options.Server.Production.Us.Site = _settings.Subdomain!.Trim();
        options.Server.Production.Eu.Site = _settings.Subdomain!.Trim();
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            // Used verbatim as the API base address.
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl.Trim();
            options.Server.Production.Eu.BaseUrl = _settings.BaseUrl.Trim();
        }

        return new MaxioAdvancedBillingClient(new HttpClient(), options);
    }

    private static void Require(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BillingException(503, $"Billing is not configured: {MaxioSettings.SectionName}:{key} is missing.");
        }
    }
}
