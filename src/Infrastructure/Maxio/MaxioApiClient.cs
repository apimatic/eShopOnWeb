using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for Maxio Advanced Billing, written against maxio-spec/openapi.yaml.
/// Base address, HTTP Basic credentials (api key as user name, "x" as password, per the spec's
/// BasicAuth security scheme) and transient-fault retries are configured by
/// <see cref="MaxioServiceCollectionExtensions.AddMaxioSubscriptionBilling"/>.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, int page, int perPage, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json" +
                   $"?page={page}&per_page={perPage}&include_archived={(includeArchived ? "true" : "false")}";

        return ListProductsAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        int page, int perPage, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var path = $"products.json?page={page}&per_page={perPage}&include_archived={(includeArchived ? "true" : "false")}";

        return ListProductsAsync(path, cancellationToken);
    }

    public async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SiteResponse>(HttpMethod.Get, "site.json", content: null, treatNotFoundAsNull: true, cancellationToken);

        return response?.Site;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest { Customer = customer };
        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", payload, treatNotFoundAsNull: false, cancellationToken);

        return response?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "customers.json",
                new[] { "Maxio returned a success status without a customer payload." }, rawBody: null);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var response = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken);

        return Unwrap(response);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest { Subscription = subscription };
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", payload, treatNotFoundAsNull: false, cancellationToken);

        return response?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "subscriptions.json",
                new[] { "Maxio returned a success status without a subscription payload." }, rawBody: null);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken);

        return response?.Subscription;
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken);

        return response?.Where(item => item.Product is not null).Select(item => item.Product!).ToArray()
            ?? (IReadOnlyList<MaxioProduct>)Array.Empty<MaxioProduct>();
    }

    private static IReadOnlyList<MaxioSubscription> Unwrap(List<SubscriptionResponse>? response) =>
        response?.Where(item => item.Subscription is not null).Select(item => item.Subscription!).ToArray()
        ?? (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>();

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method, string path, object? content, bool treatNotFoundAsNull, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, content.GetType(), options: SerializerOptions);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Maxio request {Method} {Path} timed out after {Elapsed}ms", method, path, stopwatch.ElapsedMilliseconds);
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout, method.Method, path,
                new[] { "The Maxio API did not respond before the configured timeout." }, rawBody: null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio request {Method} {Path} failed to reach the API", method, path);
            throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, method.Method, path,
                new[] { $"The Maxio API could not be reached: {ex.Message}" }, rawBody: null);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            _logger.LogInformation("Maxio {Method} {Path} responded {StatusCode} in {Elapsed}ms",
                method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errors = MaxioApiException.ParseErrors(body);
                _logger.LogWarning("Maxio {Method} {Path} returned {StatusCode}: {Errors}",
                    method, path, (int)response.StatusCode, errors.Count > 0 ? string.Join("; ", errors) : "(no detail)");

                throw new MaxioApiException(response.StatusCode, method.Method, path, errors, body);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Maxio {Method} {Path} returned a payload that does not match the specification", method, path);
                throw new MaxioApiException(response.StatusCode, method.Method, path,
                    new[] { $"The Maxio response could not be deserialized: {ex.Message}" }, body);
            }
        }
    }
}
