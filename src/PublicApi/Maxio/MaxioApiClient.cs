using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API.
/// API surface per the Billing API docs: customers, product families and subscriptions.
/// </summary>
public class MaxioApiClient
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Lists the purchasable (non-archived) products in the configured product family.
    /// GET /product_families/handle:{handle}/products.json
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = Uri.EscapeDataString(_settings.ProductFamilyHandle);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"product_families/handle:{familyHandle}/products.json?per_page=200"),
            cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<MaxioProductWrapper>>(cancellationToken);
        return (items ?? new List<MaxioProductWrapper>())
            .Select(i => i.Product)
            .Where(p => p.ArchivedAt is null)
            .ToList();
    }

    /// <summary>
    /// GET /customers/lookup.json?reference=... Returns null when no customer exists for the reference.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await response.Content.ReadFromJsonAsync<MaxioCustomerWrapper>(cancellationToken);
        return wrapper?.Customer;
    }

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the given application reference.
    /// A concurrent create losing the uniqueness race (422) is resolved by re-reading the customer.
    /// </summary>
    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(reference, email, firstName, lastName, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race against a concurrent create for the same reference; re-read.
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    /// <summary>
    /// GET /subscriptions/lookup.json?reference=... Returns null when no subscription exists for the reference.
    /// </summary>
    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await response.Content.ReadFromJsonAsync<MaxioSubscriptionWrapper>(cancellationToken);
        return wrapper?.Subscription;
    }

    /// <summary>
    /// POST /subscriptions.json — enrolls an existing customer (by reference) into a product (by handle).
    /// The subscription reference makes the enrollment idempotent from the caller's perspective.
    /// Uses "remittance" collection (invoice billed, per the Subscription Signup docs) so signup
    /// succeeds for products that do not require a payment method on file.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = "remittance"
            }
        };

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
            {
                Content = JsonContent.Create(payload)
            },
            cancellationToken);

        var wrapper = await response.Content.ReadFromJsonAsync<MaxioSubscriptionWrapper>(cancellationToken);
        return wrapper?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription payload.");
    }

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/{customerId}/subscriptions.json"),
            cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionWrapper>>(cancellationToken);
        return (items ?? new List<MaxioSubscriptionWrapper>())
            .Select(i => i.Subscription)
            .ToList();
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json")
            {
                Content = JsonContent.Create(payload)
            },
            cancellationToken);

        var wrapper = await response.Content.ReadFromJsonAsync<MaxioCustomerWrapper>(cancellationToken);
        return wrapper?.Customer
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer payload.");
    }

    /// <summary>
    /// Sends a request, retrying 429 (rate limited) responses with backoff as recommended by the
    /// Maxio error-handling guidance, and translating other failures into <see cref="MaxioApiException"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Maxio rate limit hit (429). Retrying in {Delay}s (attempt {Attempt}/{MaxAttempts}).",
                    delay.TotalSeconds, attempt + 1, MaxAttempts);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = response.StatusCode;
            response.Dispose();
            _logger.LogError("Maxio API call failed: {StatusCode} {Body}", (int)statusCode, body);
            throw new MaxioApiException(statusCode, body);
        }
    }
}
