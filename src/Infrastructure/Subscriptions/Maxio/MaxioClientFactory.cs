using System;
using System.Collections.Generic;
using System.Net.Http;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;
using AdvancedBillingEnvironment = AdvancedBilling.Standard.Environment;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Builds configured <see cref="AdvancedBillingClient"/> instances.
///
/// The client is handed an <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/> so socket and
/// DNS handling follow the host's conventions, and so the <c>Maxio:BaseUrl</c> override can be applied by
/// a delegating handler on that pipeline.
/// </summary>
public class MaxioClientFactory
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> pipeline used for Maxio traffic.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>Maxio authenticates with HTTP Basic: the API key as user name, the literal "x" as password.</summary>
    private const string ApiKeyPassword = "x";

    /// <summary>Status codes worth retrying: transient gateway, throttling and server failures.</summary>
    private static readonly IList<int> RetryableStatusCodes = new List<int> { 408, 429, 500, 502, 503, 504 };

    /// <summary>
    /// Only reads are retried. Creating a subscription is not safe to replay blindly at the transport
    /// level; that path is made idempotent deliberately, by the reference uniqueness Maxio enforces.
    /// </summary>
    private static readonly IList<HttpMethod> RetryableMethods = new List<HttpMethod> { HttpMethod.Get };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioClientFactory(IHttpClientFactory httpClientFactory, IOptionsMonitor<MaxioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public MaxioOptions Options => _options.CurrentValue;

    /// <summary>
    /// Creates a client for the current configuration.
    /// </summary>
    /// <exception cref="SubscriptionBillingNotConfiguredException">Configuration is missing or invalid.</exception>
    public AdvancedBillingClient Create()
    {
        var options = _options.CurrentValue;
        var errors = options.Validate();

        if (errors.Count > 0)
        {
            throw new SubscriptionBillingNotConfiguredException(
                "Subscription billing is not configured. " + string.Join(" ", errors));
        }

        return new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(new BasicAuthModel.Builder(options.ApiKey, ApiKeyPassword).Build())
            .Site(options.Subdomain)
            .Environment(ResolveEnvironment(options.Environment))
            .HttpClientConfig(http => http
                .HttpClientInstance(_httpClientFactory.CreateClient(HttpClientName))
                .Timeout(options.Timeout)
                .NumberOfRetries(options.RetryCount)
                .BackoffFactor(2)
                .RetryInterval(1)
                .MaximumRetryWaitTime(options.Timeout)
                .StatusCodesToRetry(RetryableStatusCodes)
                .RequestMethodsToRetry(RetryableMethods))
            .Build();
    }

    private static AdvancedBillingEnvironment ResolveEnvironment(string? environment) =>
        string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? AdvancedBillingEnvironment.EU
            : AdvancedBillingEnvironment.US;
}
