using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API (Basic auth, JSON endpoints).
/// Endpoint shapes follow the official Billing API reference.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: Maxio:ApiKey is not set.");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var items = await GetListAsync<MaxioProductEnvelope>(
                $"product_families/handle:{familyHandle}/products.json?page={page}&per_page={perPage}&include_archived=false",
                cancellationToken);

            products.AddRange(items.Select(i => i.Product));

            if (items.Count < perPage)
            {
                return products;
            }

            page++;
        }
    }

    /// <summary>
    /// Looks a customer up by the reference value assigned by this application. Returns null when no match exists.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                reference,
                email,
                first_name = firstName,
                last_name = lastName,
                organization = "eShopOnWeb"
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return envelope.Customer!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var items = await GetListAsync<MaxioSubscriptionEnvelope>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={perPage}",
                cancellationToken);

            subscriptions.AddRange(items.Select(i => i.Subscription!));

            if (items.Count < perPage)
            {
                return subscriptions;
            }

            page++;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var payload = new
        {
            uniqueness_token = Guid.NewGuid().ToString(),
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                reference,
                payment_collection_method = "remittance"
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return envelope.Subscription!;
    }

    private async Task<List<T>> GetListAsync<T>(string requestUri, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await ReadAsync<List<T>>(response, cancellationToken);
        return items ?? new List<T>();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string requestUri, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await ReadAsync<T>(response, cancellationToken);
        return result!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = new List<string>();
        if (response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                errors.Add(body);
            }
        }

        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }
}
