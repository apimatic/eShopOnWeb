using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, low-level wrapper around the Maxio Advanced Billing REST API. Handles authentication,
/// JSON (de)serialization of the wire contracts, and translating non-success responses into
/// <see cref="MaxioApiException"/>. Higher-level idempotency/orchestration logic lives in
/// <see cref="MaxioSubscriptionService"/>.
/// </summary>
internal class MaxioClient : IMaxioClient
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope!.Customer!;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await DeserializeAsync<List<ProductEnvelope>>(response, cancellationToken);
        return envelopes?.Select(e => e.Product).OfType<MaxioProduct>().Where(p => p.ArchivedAt is null).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await DeserializeAsync<List<SubscriptionEnvelope>>(response, cancellationToken);
        return envelopes?.Select(e => e.Subscription).OfType<MaxioSubscription>().ToList() ?? new List<MaxioSubscription>();
    }

    /// <summary>
    /// Attempts to create a subscription. Returns null (instead of throwing) when Maxio reports
    /// the <paramref name="uniquenessToken"/> as a duplicate submission (HTTP 409) - the caller is
    /// expected to fall back to re-reading the customer's subscriptions in that case.
    /// </summary>
    public async Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionBody
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            },
            UniquenessToken = uniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope!.Subscription!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            var isTransientFailure = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (isTransientFailure && attempt < MaxAttempts)
            {
                response.Dispose();
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            return response;
        }

        // Unreachable in practice (the loop always returns or retries), but keeps the compiler happy.
        throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, "Maxio API was unreachable after retries.");
    }

    private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
        await Task.Delay(backoff, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ExtractErrorMessageAsync(response, cancellationToken);
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio API request failed with status {(int)response.StatusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray().Select(e => e.ToString()),
                    JsonValueKind.Object => errors.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                        ? p.Value.EnumerateArray().Select(v => $"{p.Name}: {v}")
                        : new[] { $"{p.Name}: {p.Value}" }),
                    JsonValueKind.String => new[] { errors.GetString() ?? string.Empty },
                    _ => Enumerable.Empty<string>()
                };
                var joined = string.Join("; ", messages);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return joined;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through and surface the raw body below.
        }

        return body;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }
}
