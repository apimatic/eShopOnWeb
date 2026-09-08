using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioBillingClient"/> against the
/// Maxio Advanced Billing (Billing API) REST endpoints.
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private const int MaxPages = 10;
    private const int PageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(handle)}.json";
        var envelope = await GetAsync<MaxioProductFamilyEnvelope>(path, cancellationToken, allowNotFound: true);
        return envelope?.ProductFamily;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var family = await GetProductFamilyByHandleAsync(productFamilyHandle, cancellationToken);
        if (family is null)
        {
            throw new MaxioApiException(HttpStatusCode.NotFound,
                new[] { $"Product family '{productFamilyHandle}' was not found in Maxio." });
        }

        var results = new List<MaxioProduct>();
        foreach (var page in Enumerable.Range(1, MaxPages))
        {
            var path = $"product_families/{family.Id}/products.json?page={page}&per_page={PageSize}";
            var batch = await GetListAsync<MaxioProductEnvelope>(path, cancellationToken);
            results.AddRange(batch.Where(p => p.Product is not null).Select(p => p.Product!));
            if (batch.Count < PageSize)
            {
                break;
            }
        }

        return results;
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var path = $"products/handle/{Uri.EscapeDataString(handle)}.json";
        var envelope = await GetAsync<MaxioProductEnvelope>(path, cancellationToken, allowNotFound: true);
        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    public Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                reference,
                first_name = firstName,
                last_name = lastName,
                email
            }
        };
        return PostAsync<MaxioCustomerEnvelope, MaxioCustomer>("customers.json", body, cancellationToken,
            envelope => envelope.Customer ?? throw new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                new[] { "Maxio returned an empty customer payload." }));
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var results = new List<MaxioSubscription>();
        foreach (var page in Enumerable.Range(1, MaxPages))
        {
            var path = $"customers/{customerId}/subscriptions.json?page={page}&per_page={PageSize}";
            var batch = await GetListAsync<MaxioSubscriptionEnvelope>(path, cancellationToken);
            results.AddRange(batch.Where(s => s.Subscription is not null).Select(s => s.Subscription!));
            if (batch.Count < PageSize)
            {
                break;
            }
        }

        return results;
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                // The storefront does not capture payment details in this flow
                // (the plan catalog requires no card); remittance collection
                // issues an invoice instead of charging a payment profile.
                payment_collection_method = "remittance",
                // Guards against duplicate creation if a request times out and
                // is retried inside the 60-minute duplicate-prevention window.
                uniqueness_token = Guid.NewGuid().ToString()
            }
        };
        return PostAsync<MaxioSubscriptionEnvelope, MaxioSubscription>("subscriptions.json", body, cancellationToken,
            envelope => envelope.Subscription ?? throw new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                new[] { "Maxio returned an empty subscription payload." }));
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken, bool allowNotFound) where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private async Task<List<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<T>>(SerializerOptions, cancellationToken);
        return items ?? new List<T>();
    }

    private async Task<TResult> PostAsync<TEnvelope, TResult>(string path, object body, CancellationToken cancellationToken,
        Func<TEnvelope, TResult> unwrap)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<TEnvelope>(SerializerOptions, cancellationToken);
        if (envelope is null)
        {
            throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response payload." });
        }
        return unwrap(envelope);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = string.Empty;
        if (response.Content is not null)
        {
            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        throw new MaxioApiException(response.StatusCode, MaxioErrorParser.Parse(content));
    }
}
