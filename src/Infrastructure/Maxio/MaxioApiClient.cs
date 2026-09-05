using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP-level implementation of <see cref="IMaxioApiClient"/> talking to a Chargify/Maxio
/// Advanced Billing site. Authentication (Basic, API key as username) and the base address
/// are configured on the injected <see cref="HttpClient"/> at registration time.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioCustomerModel?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomerModel> CreateCustomerAsync(MaxioCreateCustomerAttributes attributes, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequestBody
        {
            Customer = attributes,
            UniquenessToken = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Customer;
    }

    public async Task<IReadOnlyList<MaxioProductModel>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<MaxioProductItemEnvelope>>(SerializerOptions, cancellationToken);
        var products = new List<MaxioProductModel>();
        if (items != null)
        {
            foreach (var item in items)
            {
                products.Add(item.Product);
            }
        }
        return products;
    }

    public async Task<MaxioSubscriptionModel> CreateSubscriptionAsync(MaxioCreateSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequestBody
        {
            Subscription = attributes,
            UniquenessToken = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionItemEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionModel>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionItemEnvelope>>(SerializerOptions, cancellationToken);
        var subscriptions = new List<MaxioSubscriptionModel>();
        if (items != null)
        {
            foreach (var item in items)
            {
                subscriptions.Add(item.Subscription);
            }
        }
        return subscriptions;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken);
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio API request failed with status {(int)response.StatusCode} ({response.StatusCode}).";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                CollectErrorStrings(errors, messages);
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }
        }
        catch (JsonException)
        {
            // fall through to raw body below
        }

        return body;
    }

    private static void CollectErrorStrings(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrorStrings(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectErrorStrings(property.Value, messages);
                }
                break;
        }
    }
}
