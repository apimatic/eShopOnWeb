using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(CreateCustomerAttributes attributes, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerEnvelope { Customer = attributes };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("products.json?per_page=200", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(cancellationToken: cancellationToken);
        return envelopes?.Select(e => e.Product).Where(p => p is not null).Select(p => p!).ToList()
            ?? new List<Product>();
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(cancellationToken: cancellationToken);
        return envelopes?.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList()
            ?? new List<Subscription>();
    }

    public async Task<Subscription> CreateSubscriptionAsync(CreateSubscriptionAttributes attributes, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionEnvelope { Subscription = attributes };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty subscription payload." });
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ExtractErrors(body);
        throw new MaxioApiException(response.StatusCode, errors);
    }

    /// <summary>
    /// Maxio's error envelope shape varies by endpoint (see errors/Error-List-Response.yaml,
    /// errors/Customer-Error-Response.yaml, errors/Error-String-Map.yaml in maxio-spec): the
    /// "errors" value can be an array of strings, a single field->message object, or a
    /// field->array-of-messages map. Flatten whichever shape shows up into plain strings.
    /// </summary>
    private static List<string> ExtractErrors(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                Flatten(errorsElement, null, messages);
            }
            else
            {
                messages.Add(body);
            }
        }
        catch (JsonException)
        {
            messages.Add(body);
        }

        return messages;
    }

    private static void Flatten(JsonElement element, string? fieldName, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                into.Add(fieldName is null ? text : $"{fieldName}: {text}");
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, fieldName, into);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, property.Name, into);
                }
                break;
            default:
                into.Add(element.GetRawText());
                break;
        }
    }
}
