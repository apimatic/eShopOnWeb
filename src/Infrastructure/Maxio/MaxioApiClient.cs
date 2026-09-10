using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Default <see cref="IMaxioApiClient"/> implementation over a typed <see cref="HttpClient"/>.
/// The client's base address and Basic-auth header are configured during DI registration. Transient
/// failures (timeouts, 429, 5xx) are retried with a short backoff, in line with Maxio's guidance to
/// slow down rather than pile on concurrency.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerEnvelope { Customer = customer };
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken);
            if (body.Contains("reference", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("taken", StringComparison.OrdinalIgnoreCase))
            {
                throw new MaxioDuplicateReferenceException(customer.Reference);
            }

            throw new BillingException($"Maxio rejected the customer creation: {body}", 422);
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new BillingException("Maxio returned an empty customer on creation.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(
                $"Maxio product family '{familyHandle}' was not found. Check the configured 'Maxio:ProductFamilyHandle'.",
                404);
        }

        await EnsureSuccessAsync(response, "list products for product family", cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken);

        var products = new List<MaxioProduct>();
        if (envelopes is not null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }
        }

        return products;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (envelopes is not null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Subscription is not null)
                {
                    subscriptions.Add(envelope.Subscription);
                }
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionEnvelope { Subscription = subscription };
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new MaxioDuplicateSubmissionException();
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken);
            throw new BillingException($"Maxio rejected the subscription creation: {body}", 422);
        }

        await EnsureSuccessAsync(response, "create subscription", cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new BillingException("Maxio returned an empty subscription on creation.");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException)
                                       && !cancellationToken.IsCancellationRequested
                                       && attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Maxio request {Method} {Path} failed (attempt {Attempt}/{Max}); retrying.",
                    request.Method, request.RequestUri, attempt, MaxAttempts);
                await Task.Delay(BackoffFor(attempt, null), cancellationToken);
                continue;
            }

            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                var delay = BackoffFor(attempt, response.Headers.RetryAfter?.Delta);
                _logger.LogWarning("Maxio request {Method} {Path} returned {Status} (attempt {Attempt}/{Max}); retrying in {Delay}ms.",
                    request.Method, request.RequestUri, (int)response.StatusCode, attempt, MaxAttempts, delay.TotalMilliseconds);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan BackoffFor(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        // 0.5s, 1.5s, ...
        return TimeSpan.FromMilliseconds(500 * ((2 * attempt) - 1));
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);
        var statusCode = response.StatusCode == HttpStatusCode.Unauthorized
            ? 502 // Don't leak an upstream auth failure as a client 401; it's a server-side misconfiguration.
            : (int)response.StatusCode;

        _logger.LogError("Maxio failed to {Action}: {Status} {Body}", action, (int)response.StatusCode, body);
        throw new BillingException($"Maxio failed to {action} ({(int)response.StatusCode}). {body}".Trim(), statusCode);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a response that could not be parsed.", 502, ex);
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
