using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing API client. Authentication is HTTP Basic with the
/// API key as username and "X" as password (configured on the HttpClient in Program.cs).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _settings.Validate();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        // Product family can be addressed by its handle using the "handle:" prefix.
        var url = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json";
        var products = await SendAsync<List<MaxioProductWrapper>>(HttpMethod.Get, url, payload: null, cancellationToken);
        return products?.Select(p => p.Product).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioCustomerRequest { Customer = customer };
        var wrapper = await SendAsync<MaxioCustomerWrapper>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return wrapper!.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string? subscriptionReference = null, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference
            }
        };
        var wrapper = await SendAsync<MaxioSubscriptionWrapper>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return wrapper!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionWrapper>>(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", payload: null, cancellationToken);
        return subscriptions?.Select(s => s.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, errorBody);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}
