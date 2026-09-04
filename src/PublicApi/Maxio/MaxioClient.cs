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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var allProducts = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page=200";
            var products = await SendAsync<List<MaxioEnvelope<MaxioProduct>>>(HttpMethod.Get, path, null, cancellationToken);
            allProducts.AddRange(products.Where(item => item.Product is not null && item.Product.ArchivedAt is null)
                .Select(item => item.Product!));
            if (products.Count < 200)
                break;
        }

        return allProducts;
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        SendOptionalAsync<MaxioEnvelope<MaxioCustomer>, MaxioCustomer>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken,
            envelope => envelope.Customer);

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await SendAsync<MaxioEnvelope<MaxioCustomer>>(HttpMethod.Post, "customers.json",
                new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
            return envelope.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no customer.");
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
        {
            var existing = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        SendOptionalAsync<MaxioEnvelope<MaxioSubscription>, MaxioSubscription>(HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken,
            envelope => envelope.Subscription);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await SendAsync<MaxioEnvelope<MaxioSubscription>>(HttpMethod.Post, "subscriptions.json",
                new MaxioCreateSubscriptionRequest { Subscription = subscription }, cancellationToken);
            return envelope.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no subscription.");
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode is 409 or 422)
        {
            // A timeout or a concurrent click can leave the create result unknown. The
            // documented reference lookup makes the operation safe to repeat.
            var existing = await FindSubscriptionByReferenceAsync(subscription.Reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await SendAsync<List<MaxioEnvelope<MaxioSubscription>>>(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        return subscriptions.Where(item => item.Subscription is not null)
            .Select(item => item.Subscription!)
            .ToArray();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateExceptionAsync(response);

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private async Task<TResult?> SendOptionalAsync<TResponse, TResult>(HttpMethod method, string path,
        CancellationToken cancellationToken, Func<TResponse, TResult?> selector)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        if (!response.IsSuccessStatusCode)
            throw await CreateExceptionAsync(response);

        var value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return value is null ? default : selector(value);
    }

    private static async Task<MaxioApiException> CreateExceptionAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
        return new MaxioApiException(response.StatusCode, $"Maxio request failed ({(int)response.StatusCode}): {detail}");
    }
}
