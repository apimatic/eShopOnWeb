using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed wrapper over the Maxio Advanced Billing REST API. Owns request/response
/// serialization, authentication (configured on the injected <see cref="HttpClient"/>), error
/// translation, and transient-fault retries. It has no orchestration logic — that lives in
/// <see cref="MaxioBillingService"/>.
/// </summary>
internal class MaxioApiClient
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Looks up a customer by their application reference. Returns <c>null</c> when no customer
    /// exists for that reference (HTTP 404), rather than throwing.
    /// </summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>Creates a customer. Throws <see cref="MaxioApiException"/> with 422 if the reference is already taken.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerEnvelope { Customer = customer };

        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json")
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer payload on create.");
    }

    /// <summary>Lists the products (plans) that belong to a product family, addressed by handle.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var uri = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";

        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new List<MaxioProductEnvelope>();

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    /// <summary>Creates a subscription for an existing customer against a product handle.</summary>
    /// <param name="uniquenessToken">Optional idempotency token; Maxio rejects a duplicate retry (409) within 60 minutes.</param>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, string? uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionEnvelope { Subscription = subscription, UniquenessToken = uniquenessToken };

        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription payload on create.");
    }

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var uri = $"customers/{customerId}/subscriptions.json?per_page=200";

        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    /// <summary>
    /// Sends a request, retrying on transient faults (HTTP 429 and 5xx, and network errors) with
    /// exponential backoff. A fresh <see cref="HttpRequestMessage"/> is built per attempt because a
    /// request message (and its content) can only be sent once. Maxio limits concurrency, so retries
    /// are sequential and backed off — never parallelized.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (attempt >= MaxAttempts || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                var delay = ComputeDelay(attempt, response);
                _logger.LogWarning(
                    $"Maxio request to {request.RequestUri} returned {(int)response.StatusCode}; retrying (attempt {attempt}/{MaxAttempts - 1}) after {delay.TotalMilliseconds:0}ms.");
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                response?.Dispose();
                var delay = ComputeDelay(attempt, null);
                _logger.LogWarning($"Maxio request failed with a network error ({ex.Message}); retrying (attempt {attempt}/{MaxAttempts - 1}) after {delay.TotalMilliseconds:0}ms.");
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                // Timeout (not caller cancellation): treat as transient.
                response?.Dispose();
                var delay = ComputeDelay(attempt, null);
                _logger.LogWarning($"Maxio request timed out; retrying (attempt {attempt}/{MaxAttempts - 1}) after {delay.TotalMilliseconds:0}ms.");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        // Honor Retry-After (delta seconds) when Maxio provides it, e.g. on 429.
        if (response?.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        // Exponential backoff: 500ms, 1000ms, ...
        return TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await TryReadErrorsAsync(response, cancellationToken);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase ?? "no detail";
        throw new MaxioApiException(response.StatusCode, $"Maxio API call failed ({(int)response.StatusCode}): {detail}", errors);
    }

    private async Task<IReadOnlyList<string>> TryReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<string>();
            }

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return ParseErrors(errorsElement);
            }

            // Fall back to the raw body (truncated) so nothing useful is lost.
            return new[] { raw.Length > 500 ? raw[..500] : raw };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ParseErrors(JsonElement errorsElement)
    {
        var messages = new List<string>();
        switch (errorsElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errorsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(item.GetString()!);
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errorsElement.EnumerateObject())
                {
                    messages.Add(property.Value.ValueKind == JsonValueKind.String
                        ? $"{property.Name}: {property.Value.GetString()}"
                        : $"{property.Name}: {property.Value}");
                }
                break;
            case JsonValueKind.String:
                messages.Add(errorsElement.GetString()!);
                break;
        }

        return messages;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}
