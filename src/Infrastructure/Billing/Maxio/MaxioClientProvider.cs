using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <inheritdoc cref="IMaxioClientProvider"/>
public sealed class MaxioClientProvider : IMaxioClientProvider
{
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/> client this integration owns. A named client keeps the
    /// timeout, primary handler and write-once handler scoped to Maxio instead of changing behaviour for
    /// every other unnamed <c>CreateClient()</c> consumer in the application.
    /// </summary>
    public const string HttpClientName = "Maxio";

    /// <summary>Maxio authenticates with the API key as the basic-auth user and a literal "x" as password.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>
    /// Bounds a single attempt. The SDK's default is 100s, which is an outage rather than a timeout on a
    /// request path.
    /// </summary>
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The lowest value the SDK accepts (Polly rejects 0), so retries cannot be disabled outright. Writes are
    /// additionally held to one send by <see cref="MaxioWriteGuard"/>.
    /// </summary>
    private const int MaxRetries = 1;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioClientProvider> _logger;
    private readonly object _gate = new();

    private MaxioAdvancedBillingClient? _client;

    public MaxioClientProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<MaxioSettings> settings,
        ILogger<MaxioClientProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public MaxioAdvancedBillingClient GetClient()
    {
        // Double-checked so the happy path is lock-free; the client is immutable once built and is safe to
        // use concurrently across requests.
        var client = _client;
        if (client is not null)
        {
            return client;
        }

        lock (_gate)
        {
            return _client ??= Build(_settings.CurrentValue);
        }
    }

    private MaxioAdvancedBillingClient Build(MaxioSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            var detail = string.Join(" ", errors);
            _logger.LogError("Maxio billing is not configured: {Errors}", detail);
            throw new BillingNotConfiguredException(
                "Subscription billing is not configured on this deployment. " + detail);
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!.Trim(),
                Password = ApiKeyPasswordPlaceholder
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = MaxRetries,
                Timeout = PerAttemptTimeout
            }
        };

        // The base URL is a "{site}" template that is expanded per request. Setting the site is not optional:
        // when it is left at its default the SDK silently calls a literal "subdomain" host.
        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain.Trim();
        }

        // An explicit base URL wins outright: a value with no "{site}" placeholder is used verbatim.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
        }

        _logger.LogInformation(
            "Maxio billing client configured for base URL template {BaseUrl} (site {Site}), product family {ProductFamilyHandle}.",
            options.Server.Production.Us.BaseUrl,
            options.Server.Production.Us.Site,
            settings.ProductFamilyHandle);

        return new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(HttpClientName), options);
    }
}
