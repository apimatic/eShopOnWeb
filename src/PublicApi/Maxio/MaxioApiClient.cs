using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private const int PageSize = 200;
    private const int MaxPages = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MaxioOptions _options;
    private readonly HttpClient _httpClient;
    private string? _baseUrl;

    public MaxioApiClient(IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            Timeout = TimeSpan.FromSeconds(100),
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb/1.0");
    }

    public async Task<MaxioProductFamily> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(handle));
        }

        var path = $"product_families/{Uri.EscapeDataString($"handle:{handle}")}.json";
        var envelope = await SendAsync<MaxioProductFamilyEnvelope>(HttpMethod.Get, path, cancellationToken: cancellationToken);
        return envelope?.ProductFamily
            ?? throw new MaxioApiException(404, $"Maxio returned no product family for handle '{handle}'.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var query = $"per_page={PageSize}&page={page}";
            var path = $"product_families/{productFamilyId}/products.json?{query}";
            var pageProducts = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, cancellationToken: cancellationToken) ?? new List<MaxioProductEnvelope>();
            if (pageProducts.Count == 0)
            {
                break;
            }

            foreach (var envelope in pageProducts)
            {
                if (envelope.Product != null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (pageProducts.Count < PageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            path,
            allowNotFound: true,
            cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerProfile customer, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerEnvelope { Customer = customer };
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(500, "Maxio returned an empty response when creating the customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, cancellationToken: cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();
        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription != null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionData subscription, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionEnvelope { Subscription = subscription };
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(500, "Maxio returned an empty response when creating the subscription.");
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default,
        bool allowNotFound = false)
    {
        using var request = BuildRequest(method, path, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        var errorBody = await ReadErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
        throw new MaxioApiException((int)response.StatusCode, errorBody);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body)
    {
        var baseUrl = EnsureBaseUrl();
        var request = new HttpRequestMessage(method, $"{baseUrl}/{path}");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{EnsureApiKey()}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private string EnsureBaseUrl()
    {
        return _baseUrl ??= _options.BuildApiBaseUrl();
    }

    private string EnsureApiKey()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException(
                $"Maxio is not configured: '{MaxioOptions.SectionName}:ApiKey' (env MAXIO_API_KEY) is required.");
        }

        return _options.ApiKey;
    }

    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return FlattenErrorBody(raw);
        }
        catch
        {
            return $"Maxio request failed with HTTP {(int)response.StatusCode}.";
        }
    }

    /// <summary>
    /// Maxio error bodies vary: sometimes a bare string, sometimes
    /// { "errors": "..." }, { "errors": ["...", ...] } or { "errors": { field: [messages] } }.
    /// This flattens the most useful message text out of those shapes.
    /// </summary>
    private static string FlattenErrorBody(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                return FlattenJsonElement(errors);
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? raw;
            }

            return raw.Length > 2000 ? raw[..2000] : raw;
        }
        catch (JsonException)
        {
            return raw.Length > 2000 ? raw[..2000] : raw;
        }
    }

    private static string FlattenJsonElement(JsonElement element)
    {
        var messages = new List<string>();
        FlattenInto(element, messages);
        return messages.Count == 0 ? string.Empty : string.Join(" ", messages);
    }

    private static void FlattenInto(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    FlattenInto(item, messages);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenInto(property.Value, messages);
                }

                break;
        }
    }
}
