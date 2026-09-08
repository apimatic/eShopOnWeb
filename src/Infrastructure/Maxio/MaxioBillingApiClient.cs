using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing REST API. Callers authenticate via HTTP
/// Basic (API key as username, "X" as password) — the Authorization header is pre-configured
/// on the injected <see cref="HttpClient"/> at registration time.
/// </summary>
public sealed class MaxioBillingApiClient
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioBillingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Looks a customer up by its application reference. Returns null when not found.</summary>
    internal async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        string content = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
        return Deserialize<CustomerEnvelope>(content).Customer;
    }

    internal async Task<MaxioCustomer> CreateCustomerAsync(CustomerAttributes attributes, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, "customers.json", new CreateCustomerRequest { Customer = attributes }, cancellationToken)
            .ConfigureAwait(false);

        string content = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
        var customer = Deserialize<CustomerEnvelope>(content).Customer;
        return customer ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer payload.");
    }

    internal async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken).ConfigureAwait(false);
        string content = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);

        var envelopes = Deserialize<List<SubscriptionEnvelope>>(content);
        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription != null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }
        return subscriptions;
    }

    internal async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, "subscriptions.json",
                new CreateSubscriptionRequest { Subscription = attributes, UniquenessToken = uniquenessToken }, cancellationToken)
            .ConfigureAwait(false);

        string content = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
        var subscription = Deserialize<SubscriptionEnvelope>(content).Subscription;
        return subscription ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription payload.");
    }

    /// <summary>Finds the product family whose handle matches. Returns null when not found.</summary>
    internal async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("product_families.json", cancellationToken).ConfigureAwait(false);
        string content = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);

        var envelopes = Deserialize<List<ProductFamilyEnvelope>>(content);
        foreach (var envelope in envelopes)
        {
            if (envelope.ProductFamily != null &&
                string.Equals(envelope.ProductFamily.Handle, handle, StringComparison.OrdinalIgnoreCase))
            {
                return envelope.ProductFamily;
            }
        }
        return null;
    }

    internal async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"product_families/{productFamilyId}/products.json", cancellationToken).ConfigureAwait(false);
        string content = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);

        var envelopes = Deserialize<List<ProductEnvelope>>(content);
        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product != null)
            {
                products.Add(envelope.Product);
            }
        }
        return products;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, SummarizeError(response, content));
        }

        return content;
    }

    private static string SummarizeError(HttpResponseMessage response, string content)
    {
        string parsed = TryExtractErrorMessages(content);
        if (!string.IsNullOrWhiteSpace(parsed))
        {
            return parsed;
        }

        string raw = string.IsNullOrWhiteSpace(content)
            ? $"(no response body) HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : content.Length <= 400 ? content : content[..400] + "...";
        return $"Maxio request failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {raw}";
    }

    private static string TryExtractErrorMessages(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                var messages = new List<string>();
                CollectMessages(errorsElement, messages);
                return messages.Count > 0 ? string.Join(" ", messages) : string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw-body summary.
        }

        return string.Empty;
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (!string.IsNullOrWhiteSpace(element.GetString()))
                {
                    messages.Add(element.GetString()!);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectMessages(property.Value, messages);
                }
                break;
        }
    }

    private static T Deserialize<T>(string content) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions) ?? new T();
    }
}
