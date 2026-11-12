using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Builds the Maxio SDK client once from the <c>Maxio:</c> settings. The options are captured at the
/// first use, so a rotated secret takes effect after a restart.
/// </summary>
public class MaxioClientProvider
{
    private readonly IOptions<MaxioSettings> _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _gate = new();
    private MaxioAdvancedBillingClient? _client;

    public MaxioClientProvider(IOptions<MaxioSettings> settings, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Names of the required settings that are missing or blank.</summary>
    public static IReadOnlyList<string> MissingSettings(MaxioSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) missing.Add("Maxio:ApiKey");
        if (string.IsNullOrWhiteSpace(settings.Subdomain)) missing.Add("Maxio:Subdomain");
        if (string.IsNullOrWhiteSpace(settings.Environment)) missing.Add("Maxio:Environment");
        return missing;
    }

    public MaxioAdvancedBillingClient GetClient()
    {
        if (_client != null) return _client;
        lock (_gate)
        {
            return _client ??= Build(_settings.Value);
        }
    }

    private MaxioAdvancedBillingClient Build(MaxioSettings settings)
    {
        var missing = MissingSettings(settings);
        if (missing.Count > 0)
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable,
                $"Billing is not configured: missing {string.Join(", ", missing)}.");
        }

        ServerEnvironment environment;
        switch (settings.Environment!.Trim().ToUpperInvariant())
        {
            case "US": environment = ServerEnvironment.Us; break;
            case "EU": environment = ServerEnvironment.Eu; break;
            default:
                throw new BillingException(HttpStatusCode.ServiceUnavailable,
                    "Billing is not configured: Maxio:Environment must be US or EU.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            // The username is the Maxio API key, the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            Environment = environment,
            // Interactive request path: bound each attempt and the number of attempts.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20), MaxRetries = 2 },
        };
        options.Logging = options.Logging with { LoggerFactory = _loggerFactory };

        var subdomain = settings.Subdomain!.Trim();
        options.Server.Production.Us.Site = subdomain;
        options.Server.Production.Eu.Site = subdomain;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Verbatim: nothing is derived from the subdomain.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(), options);
    }
}
