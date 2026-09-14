using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin, typed HTTP client for the Maxio Advanced Billing REST API.
/// See https://docs.maxio.com for the underlying API surface. All methods
/// operate against the site configured via <see cref="MaxioOptions"/>.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, string uniquenessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> ReadSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken);
}

public sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        using var document = await GetJsonAsync(path, allowNotFound: false, cancellationToken);

        var products = new List<MaxioProduct>();
        foreach (var item in EnumerateItems(document.RootElement))
        {
            var product = DeserializeWrapped<MaxioProductItem>(item)?.Product;
            if (product is not null)
            {
                products.Add(product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var document = await GetJsonAsync(path, allowNotFound: true, cancellationToken);
        if (document is null)
        {
            return null;
        }

        return DeserializeWrapped<MaxioCustomerEnvelope>(document.RootElement)?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var payload = new MaxioCustomerEnvelope { Customer = customer };
        using var document = await PostJsonAsync("customers.json", payload, cancellationToken);
        return DeserializeWrapped<MaxioCustomerEnvelope>(document.RootElement)?.Customer
            ?? throw new MaxioApiException(422, "Maxio created a customer but returned an empty response.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new MaxioSubscriptionCreateEnvelope
        {
            Subscription = subscription,
            UniquenessToken = uniquenessToken
        };

        using var document = await PostJsonAsync("subscriptions.json", payload, cancellationToken);
        return DeserializeSubscription(document.RootElement)
            ?? throw new MaxioApiException(422, "Maxio created a subscription but returned an empty response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var document = await GetJsonAsync(path, allowNotFound: false, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var item in EnumerateItems(document.RootElement))
        {
            var subscription = DeserializeSubscription(item);
            if (subscription is not null)
            {
                subscriptions.Add(subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> ReadSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/{subscriptionId}.json";
        using var document = await GetJsonAsync(path, allowNotFound: false, cancellationToken);
        return DeserializeSubscription(document.RootElement)
            ?? throw new MaxioApiException(404, $"Maxio subscription {subscriptionId} was not found.");
    }

    // -----------------------------------------------------------------------
    // Transport helpers
    // -----------------------------------------------------------------------

    private async Task<JsonDocument?> GetJsonAsync(string path, bool allowNotFound, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var response = await SendAsync(request, allowNotFound, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        return await ReadResponseAsync(response, cancellationToken);
    }

    private async Task<JsonDocument?> PostJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var response = await SendAsync(request, allowNotFound: false, cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool allowNotFound, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;
        response.Dispose();

        throw new MaxioApiException(statusCode, BuildErrorMessage(statusCode, body), body);
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        response.Dispose();
        return document;
    }

    // -----------------------------------------------------------------------
    // Parsing helpers. The Billing API returns list payloads as either a bare
    // array ([ { "<resource>": {...} }, ... ]) or, on some paths, wrapped in an
    // object ({ "items": [ ... ] }); single resources are wrapped as
    // { "<resource>": {...} }. Both shapes are handled here.
    // -----------------------------------------------------------------------

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in root.EnumerateArray())
                {
                    yield return element;
                }
                yield break;
            case JsonValueKind.Object:
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in items.EnumerateArray())
                    {
                        yield return element;
                    }
                }
                yield break;
        }
    }

    private static T? DeserializeWrapped<T>(JsonElement element) where T : class
    {
        return element.ValueKind == JsonValueKind.Object
            ? element.Deserialize<T>(JsonOptions)
            : null;
    }

    private static MaxioSubscription? DeserializeSubscription(JsonElement element)
    {
        var envelope = DeserializeWrapped<MaxioSubscriptionEnvelope>(element);
        var payload = envelope?.Subscription;
        if (payload is null || payload.Id is null)
        {
            return null;
        }

        return new MaxioSubscription
        {
            Id = payload.Id.Value,
            State = payload.State ?? string.Empty,
            ProductHandle = payload.Product?.Handle,
            ProductName = payload.Product?.Name,
            PriceInCents = payload.ProductPriceInCents ?? payload.Product?.PriceInCents,
            CurrentPeriodEndsAt = payload.CurrentPeriodEndsAt,
            NextAssessmentAt = payload.NextAssessmentAt,
            ActivatedAt = payload.ActivatedAt,
            CreatedAt = payload.CreatedAt,
            CustomerId = payload.Customer?.Id ?? 0
        };
    }

    private static string BuildErrorMessage(int statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio API returned HTTP {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                CollectErrorMessages(errors, messages);
                if (messages.Count > 0)
                {
                    return $"Maxio API returned HTTP {statusCode}: {string.Join("; ", messages)}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"Maxio API returned HTTP {statusCode}: {body}";
    }

    private static void CollectErrorMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrorMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectErrorMessages(property.Value, messages);
                }
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text) && !messages.Contains(text))
                {
                    messages.Add(text);
                }
                break;
        }
    }
}
