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
/// Low-level access to the Maxio Advanced Billing (Billing) API.
/// Endpoint/behavior reference: Maxio Billing API documentation (docs.maxio.com).
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists all non-paginated-unfriendly products of the site, across all product families.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListAllProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer whose <c>reference</c> equals the given value, or null when none exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. If Maxio rejects the create because a customer with the same reference
    /// already exists (e.g. a concurrent create), the existing customer is looked up and returned instead.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription. Throws <see cref="MaxioApiException"/> with
    /// <see cref="MaxioApiException.IsDuplicateSubmission"/> when the uniqueness token conflicts.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}

public class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;
    private const string BasicPassword = "X";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _options.Validate();

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_options.EffectiveBaseUrl + "/");
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:{BasicPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListAllProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
                $"products.json?page={page}&per_page={PageSize}", cancellationToken);

            products.AddRange(envelopes.Where(e => e.Product != null).Select(e => e.Product!));
            if (envelopes.Count < PageSize)
            {
                return products;
            }
            page++;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);
            var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
            return envelope?.Customer;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { customer = request }, SerializerOptions);
        using var response = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio allows at most one customer per reference value. A concurrent create may have
            // won the race; resolve idempotently by looking the customer up.
            if (request.Reference != null)
            {
                var existing = await FindCustomerByReferenceAsync(request.Reference, cancellationToken);
                if (existing != null)
                {
                    return existing;
                }
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken),
                "Maxio returned no customer in the create-customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;
        while (true)
        {
            var response = await _httpClient.GetAsync(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={PageSize}", cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var envelopes = DeserializeList(content)
                    .Where(e => e.Subscription != null)
                    .Select(e => e.Subscription!)
                    .ToList();

                subscriptions.AddRange(envelopes);
                if (envelopes.Count < PageSize)
                {
                    return subscriptions;
                }
                page++;
            }
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { subscription = request }, SerializerOptions);
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken),
                "Maxio returned no subscription in the create-subscription response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, string jsonBody, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await DeserializeAsync<T>(response, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken), "Maxio returned an empty response body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, body);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(content, SerializerOptions);
    }

    /// <summary>
    /// Some Maxio list endpoints return a bare array of envelopes, others an
    /// { "items": [...] } wrapper; parse either shape.
    /// </summary>
    private static List<MaxioSubscriptionEnvelope> DeserializeList(string content)
    {
        var bare = JsonSerializer.Deserialize<List<MaxioSubscriptionEnvelope>>(content, SerializerOptions);
        if (bare != null && (bare.Count > 0 || !content.Contains("\"items\"")))
        {
            return bare;
        }

        var wrapped = JsonSerializer.Deserialize<MaxioSubscriptionListEnvelope>(content, SerializerOptions);
        return wrapped?.Items ?? new List<MaxioSubscriptionEnvelope>();
    }
}
