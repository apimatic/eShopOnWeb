using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Lazily builds the Maxio Advanced Billing client on first use, validating configuration then.
/// Subscription billing is an additive capability hosted inside an app that also runs pure
/// one-time commerce (and test hosts), so a missing configuration must not prevent the host from
/// starting — instead the first billing call fails with an actionable, caller-safe error.
/// The client is built once and reused for the process lifetime.
/// </summary>
public sealed class MaxioClientProvider
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IConfiguration? _configuration;

    private MaxioAdvancedBillingClient? _client;
    private MaxioSettings? _settings;

    public MaxioClientProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    private MaxioClientProvider(MaxioAdvancedBillingClient client, MaxioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    /// <summary>Test seam: a provider backed by an already-constructed client (e.g. over a stub handler).</summary>
    public static MaxioClientProvider ForTesting(MaxioAdvancedBillingClient client, MaxioSettings settings) =>
        new(client, settings);

    public MaxioSettings GetSettings() => Resolve().settings;

    public MaxioAdvancedBillingClient GetClient() => Resolve().client;

    private (MaxioSettings settings, MaxioAdvancedBillingClient client) Resolve()
    {
        if (_client is not null && _settings is not null)
        {
            return (_settings, _client);
        }

        lock (this)
        {
            if (_client is not null && _settings is not null)
            {
                return (_settings, _client);
            }

            var settings = _configuration!.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw NotConfigured("'Maxio:ApiKey' is missing (set it from the MAXIO_API_KEY environment variable, e.g. via user secrets).");
            }
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw NotConfigured("'Maxio:Subdomain' is missing (set it from the MAXIO_SITE_SUBDOMAIN environment variable).");
            }
            if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
            {
                throw NotConfigured("'Maxio:ProductFamilyHandle' is missing (set it from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
            }

            // NOTE: create-subscription and create-customer are POSTs, so the SDK's status-trigger
            // never retries them; a transport failure (connection reset) CAN however be resent on
            // any verb. Both writes carry client-supplied references that Maxio enforces unique,
            // so a resent create is harmless: the duplicate attempt fails with a 422 and the code
            // reconciles by re-running the reference lookup.
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = string.Equals(settings.Environment?.Trim(), "eu", StringComparison.OrdinalIgnoreCase)
                    ? ServerEnvironment.Eu
                    : ServerEnvironment.Us,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                }
            };

            // Configure the subdomain and (optional) base-URL override on both hosted environments;
            // only the one selected via options.Environment is ever read.
            options.Server.Production.Us.Site = settings.Subdomain;
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }

            var httpClient = _httpClientFactory!.CreateClient(HttpClientName);
            _settings = settings;
            _client = new MaxioAdvancedBillingClient(httpClient, options);
            return (settings, _client);
        }
    }

    private static InvalidOperationException NotConfigured(string detail) =>
        new($"Maxio billing is not configured: {detail}");
}
