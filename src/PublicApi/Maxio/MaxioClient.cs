using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Hand-written client built directly against the Maxio OpenAPI specification.
/// Auth: HTTP Basic with the API key as username and "x" as password
/// (per the spec's BasicAuth security scheme); configured on the typed HttpClient.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxPages = 20; // safety bound for paginated list endpoints
    private const int PerPage = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pageItems = await GetAsync<List<MaxioProductResponse>>(
                $"products.json?page={page}&per_page={PerPage}", cancellationToken);

            if (pageItems.Count == 0)
            {
                break;
            }

            foreach (var item in pageItems)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (pageItems.Count < PerPage)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json", request, cancellationToken);

        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, Array.Empty<string>(), "Empty customer in Maxio response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", request, cancellationToken);

        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, Array.Empty<string>(), "Empty subscription in Maxio response.");
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var result = await ReadAsync<T>(response, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, Array.Empty<string>(), "Empty Maxio response body.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);
        var result = await ReadAsync<TResponse>(response, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, Array.Empty<string>(), "Empty Maxio response body.");
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ExtractErrors(body);
        _logger.LogWarning("Maxio request failed with {StatusCode}: {Errors}", (int)response.StatusCode,
            errors.Count > 0 ? string.Join("; ", errors) : body);
        throw new MaxioApiException(response.StatusCode, errors, body);
    }

    // Parses the spec's Error-List-Response ({ "errors": [...] }); the errors node
    // can be an array of strings or an object of field/message pairs depending on
    // the endpoint, so both shapes are flattened to plain messages.
    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return errors;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                switch (errorsElement.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errorsElement.EnumerateArray())
                        {
                            errors.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText());
                        }
                        break;
                    case JsonValueKind.Object:
                        foreach (var property in errorsElement.EnumerateObject())
                        {
                            var value = property.Value.ValueKind == JsonValueKind.String
                                ? property.Value.GetString()
                                : property.Value.GetRawText();
                            errors.Add($"{property.Name}: {value}");
                        }
                        break;
                    case JsonValueKind.String:
                        errors.Add(errorsElement.GetString()!);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; the raw body is carried on the exception.
        }

        return errors;
    }
}
