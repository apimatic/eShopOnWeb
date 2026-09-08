using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API. Endpoint URLs, query parameters, payload
/// shapes and the Basic-auth scheme (username = API key, password = "x") are taken verbatim from the
/// Maxio OpenAPI specification in <c>maxio-spec/</c>. The base address is resolved from
/// <see cref="MaxioOptions"/> (honoring the <c>Maxio:BaseUrl</c> override) on every request so that
/// configuration can never go stale.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private const string JsonContentType = "application/json";
    private const string BasicAuthPassword = "x";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, MaxioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioProductFamilyEnvelope> envelopes = await GetManyAsync<MaxioProductFamilyEnvelope>("product_families.json", cancellationToken).ConfigureAwait(false);
        return envelopes.Where(e => e.ProductFamily is not null).Select(e => e.ProductFamily!).ToList();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioProductEnvelope> envelopes = await GetManyAsync<MaxioProductEnvelope>($"product_families/{productFamilyId}/products.json", cancellationToken).ConfigureAwait(false);
        return envelopes.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    public async Task<MaxioProduct?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"products/handle/{Uri.EscapeDataString(productHandle)}.json");
        return await ReadEnvelopeOrNullAsync<MaxioProduct>(request, "product", cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        return await ReadEnvelopeOrNullAsync<MaxioCustomer>(request, "customer", cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Post, "customers.json", new MaxioCreateCustomerRequest(customer));
        return (await ReadEnvelopePropertyAsync<MaxioCustomer>(request, "customer", cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioSubscriptionEnvelope> envelopes = await GetManyAsync<MaxioSubscriptionEnvelope>($"customers/{customerId}/subscriptions.json", cancellationToken).ConfigureAwait(false);
        return envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}");
        return await ReadEnvelopeOrNullAsync<MaxioSubscription>(request, "subscription", cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Post, "subscriptions.json", new MaxioCreateSubscriptionRequest(subscription));
        return (await ReadEnvelopePropertyAsync<MaxioSubscription>(request, "subscription", cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<IReadOnlyList<TEnvelope>> GetManyAsync<TEnvelope>(string relativeUrl, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Get, relativeUrl);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, "The Maxio API returned an error.", ExtractErrors(body));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<TEnvelope>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<TEnvelope>>(body, JsonOptions) ?? new List<TEnvelope>();
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, "The Maxio API returned an unexpected response body.", new[] { ex.Message });
        }
    }

    /// <summary>
    /// Reads a single entity envelope, treating an HTTP 404 (resource not found) as <c>null</c>.
    /// Used by the lookup-by-reference and read-by-handle endpoints.
    /// </summary>
    private async Task<T?> ReadEnvelopeOrNullAsync<T>(HttpRequestMessage request, string propertyName, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadEnvelopePropertyAsync<T>(request, propertyName, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return default;
        }
    }

    private async Task<T?> ReadEnvelopePropertyAsync<T>(HttpRequestMessage request, string propertyName, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, "The Maxio API returned an error.", ExtractErrors(body));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            {
                return default;
            }

            return element.Deserialize<T>(JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, "The Maxio API returned an unexpected response body.", new[] { ex.Message });
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, object? payload = null)
    {
        var request = new HttpRequestMessage(method, BuildUrl(relativeUrl));

        string basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:{BasicAuthPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);

        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions))
            {
                Headers = { ContentType = new MediaTypeHeaderValue(JsonContentType) }
            };
        }

        return request;
    }

    private Uri BuildUrl(string relativeUrl)
    {
        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out Uri? absolute))
        {
            return absolute;
        }

        return new Uri($"{_options.ResolveApiBaseUrl()}/{relativeUrl}");
    }

    /// <summary>
    /// Parses the heterogeneous error envelopes used across the Maxio spec
    /// (e.g. <c>Error-List-Response</c>, <c>Single-Error-Response</c> and field-keyed maps).
    /// </summary>
    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(body))
        {
            return errors;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out JsonElement errorsProperty))
            {
                CollectStrings(errors, errorsProperty);
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement errorProperty))
            {
                CollectStrings(errors, errorProperty);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                CollectStrings(errors, root);
            }
            else if (errors.Count == 0)
            {
                errors.Add(body);
            }
        }
        catch (JsonException)
        {
            errors.Add(body);
        }

        return errors;
    }

    private static void CollectStrings(List<string> errors, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectStrings(errors, item);
                }
                break;

            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    CollectKeyedStrings(errors, property.Name, property.Value);
                }
                break;

            case JsonValueKind.String:
                string? text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    errors.Add(text);
                }
                break;
        }
    }

    private static void CollectKeyedStrings(List<string> errors, string key, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array)
        {
            var nested = new List<string>();
            CollectStrings(nested, element);
            foreach (string message in nested)
            {
                errors.Add($"{key}: {message}");
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}
