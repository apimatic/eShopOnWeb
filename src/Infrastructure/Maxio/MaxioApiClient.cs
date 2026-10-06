using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing REST API (verified against the live API:
/// Basic auth with the API key as username and literal "x" as password; snake_case JSON with
/// wrapper keys; list endpoints return arrays of wrapped objects).
/// </summary>
public class MaxioApiClient
{
    private static readonly int[] RetryableStatusCodes =
    {
        (int)HttpStatusCode.RequestTimeout,      // 408
        (int)HttpStatusCode.TooManyRequests,     // 429
        (int)HttpStatusCode.InternalServerError, // 500
        (int)HttpStatusCode.BadGateway,          // 502
        (int)HttpStatusCode.ServiceUnavailable,  // 503
        (int)HttpStatusCode.GatewayTimeout       // 504
    };

    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// GET /product_families/handle:{handle}/products.json — lists the plans (products) in the
    /// configured product family, following pagination.
    /// </summary>
    public virtual async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        return await ListPagedAsync<MaxioProductWrapper, MaxioProduct>(
            path, w => w.Product, cancellationToken);
    }

    /// <summary>
    /// GET /products/handle/{handle}.json — fetches a single product by its handle.
    /// Returns null when the product does not exist (404).
    /// </summary>
    public virtual async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var product = await GetAsync<MaxioProductWrapper>(path, notFoundIsNull: true, cancellationToken);
        return product?.Product;
    }

    /// <summary>
    /// GET /customers/lookup.json?reference={reference} — returns the customer with the given
    /// reference, or null when no customer matches (404).
    /// </summary>
    public virtual async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await GetAsync<MaxioCustomerWrapper>(path, notFoundIsNull: true, cancellationToken);
        return wrapper?.Customer;
    }

    /// <summary>
    /// POST /customers.json — creates a customer.
    /// </summary>
    public virtual async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, string? organization, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Organization = organization
            }
        };

        var wrapper = await SendAsync<MaxioCustomerWrapper>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return wrapper.Customer;
    }

    /// <summary>
    /// GET /subscriptions.json?customer_id={id} — lists a customer's subscriptions, following pagination.
    /// </summary>
    public virtual async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"subscriptions.json?customer_id={customerId}";
        return await ListPagedAsync<MaxioSubscriptionWrapper, MaxioSubscription>(
            path, w => w.Subscription, cancellationToken);
    }

    /// <summary>
    /// POST /subscriptions.json — enrolls a customer in a product.
    /// </summary>
    public virtual async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var wrapper = await SendAsync<MaxioSubscriptionWrapper>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return wrapper.Subscription;
    }

    private async Task<T?> GetAsync<T>(string path, bool notFoundIsNull, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);
        }
        catch (MaxioApiException ex) when (notFoundIsNull && ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<TItem>> ListPagedAsync<TWrapper, TItem>(string path, Func<TWrapper, TItem> unwrap, CancellationToken cancellationToken)
        where TWrapper : class
    {
        const int perPage = 200;
        var results = new List<TItem>();
        var page = 1;
        while (true)
        {
            var separator = path.Contains('?') ? '&' : '?';
            var pagePath = $"{path}{separator}page={page}&per_page={perPage}";
            var wrappers = await SendAsync<List<TWrapper>>(HttpMethod.Get, pagePath, null, cancellationToken);
            results.AddRange(wrappers.Select(unwrap));
            if (wrappers.Count < perPage)
            {
                return results;
            }
            page++;
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var jsonBody = body is null ? null : JsonSerializer.Serialize(body, MaxioJson.Serializer);

        for (var attempt = 0; ; attempt++)
        {
            var response = await SendOnceAsync(method, path, jsonBody, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return JsonSerializer.Deserialize<T>(stream, MaxioJson.Serializer)
                    ?? throw new MaxioApiException((int)response.StatusCode, "Maxio API returned an empty response.", null);
            }

            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (RetryableStatusCodes.Contains((int)response.StatusCode) && attempt < RetryDelays.Length)
            {
                var delay = GetRetryDelay(response, RetryDelays[attempt]);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            throw new MaxioApiException((int)response.StatusCode, ParseErrorMessage(response.StatusCode, rawBody), rawBody);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, TimeSpan fallback)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }
        return fallback;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, string? jsonBody, CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey;
        var request = new HttpRequestMessage(method, path);
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        return response;
    }

    /// <summary>
    /// Maxio reports errors in several shapes: {"errors": ["msg", ...]},
    /// {"errors": {"field": ["msg", ...]}}, {"error": "msg"}, or a plain body.
    /// </summary>
    private static string ParseErrorMessage(HttpStatusCode statusCode, string rawBody)
    {
        var message = TryParseErrorMessage(rawBody);
        return message ?? $"Maxio API request failed with status {(int)statusCode}.";
    }

    private static string? TryParseErrorMessage(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("errors", out var errors))
                {
                    switch (errors.ValueKind)
                    {
                        case JsonValueKind.Array:
                            var items = errors.EnumerateArray()
                                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                                .Where(s => !string.IsNullOrWhiteSpace(s));
                            var joined = string.Join("; ", items);
                            return string.IsNullOrWhiteSpace(joined) ? null : joined;
                        case JsonValueKind.Object:
                            var parts = errors.EnumerateObject()
                                .Select(p => $"{p.Name}: {string.Join("; ", p.Value.ValueKind == JsonValueKind.Array
                                    ? p.Value.EnumerateArray().Select(v => v.ToString())
                                    : new[] { p.Value.ToString() })}");
                            var joinedParts = string.Join("; ", parts);
                            return string.IsNullOrWhiteSpace(joinedParts) ? null : joinedParts;
                        case JsonValueKind.String:
                            return errors.GetString();
                    }
                }

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }
            }
            else if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        var truncated = rawBody.Length > 500 ? rawBody[..500] : rawBody;
        return truncated;
    }
}