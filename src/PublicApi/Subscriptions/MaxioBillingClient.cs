using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page=1&per_page=200&include_archived=false",
            null,
            cancellationToken);

        var products = new List<MaxioProduct>();
        foreach (var envelope in response ?? new List<MaxioProductEnvelope>())
        {
            if (envelope.Product is { ArchivedAt: null } product)
                products.Add(product);
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            notFoundIsNull: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);
        return response!.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            notFoundIsNull: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);
        return response!.Subscription;
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(long id, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/{id}.json",
            null,
            cancellationToken);
        return response!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        // The current endpoint normally returns a single subscription object for one result,
        // despite the older portal schema describing the response as an array. Accept both
        // wire shapes so account reads remain compatible across Maxio site/API versions.
        var json = await SendJsonAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in json.EnumerateArray())
                AddSubscription(item, subscriptions);
        }
        else if (json.ValueKind == JsonValueKind.Object)
        {
            AddSubscription(json, subscriptions);
        }

        return subscriptions;
    }

    private static void AddSubscription(JsonElement item, ICollection<MaxioSubscription> subscriptions)
    {
        var subscriptionJson = item.TryGetProperty("subscription", out var wrapped)
            ? wrapped
            : item;
        var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionJson.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (subscription is not null)
            subscriptions.Add(subscription);
    }

    private async Task<JsonElement> SendJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException((int)response.StatusCode, $"Maxio request failed with HTTP {(int)response.StatusCode}.");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool notFoundIsNull = false)
        where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            return default;

        if (!response.IsSuccessStatusCode)
        {
            // Do not include the upstream body in the exception: it may contain customer or payment data.
            throw new MaxioApiException((int)response.StatusCode, $"Maxio request failed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty response.");
    }
}
