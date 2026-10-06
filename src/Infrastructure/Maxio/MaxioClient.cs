using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing, built against maxio-spec/openapi.yaml.
/// Auth: Basic scheme, username = API key, password = literal "x" (securitySchemes: BasicAuth).
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private const int ProductsPageSize = 100;
    private const int MaxProductPages = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;
        maxioOptions.Validate();

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(maxioOptions.ResolveBaseUrl() + "/");
        var credentialBytes = Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialBytes));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            return ParseEnvelope<MaxioCustomer>(response, "customer");
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { customer = new { first_name = customer.FirstName, last_name = customer.LastName, email = customer.Email, reference = customer.Reference } }, JsonOptions);
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, "customers.json") { Content = JsonContent(body) }, cancellationToken);
        return ParseEnvelope<MaxioCustomer>(response, "customer")!;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxProductPages; page++)
        {
            var response = await GetAsync($"products.json?page={page}&per_page={ProductsPageSize}", cancellationToken);
            var batch = JsonSerializer.Deserialize<List<MaxioProductEnvelope>>(response, JsonOptions) ?? new();
            products.AddRange(batch.Select(b => b.Product!).Where(p => p is not null));
            if (batch.Count < ProductsPageSize)
            {
                break;
            }
        }
        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, string? paymentCollectionMethod = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["product_handle"] = productHandle,
            ["customer_id"] = customerId,
            ["reference"] = reference
        };
        if (paymentCollectionMethod is not null)
        {
            payload["payment_collection_method"] = paymentCollectionMethod;
        }
        var body = JsonSerializer.Serialize(new { subscription = payload }, JsonOptions);
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, "subscriptions.json") { Content = JsonContent(body) }, cancellationToken);
        return ParseEnvelope<MaxioSubscription>(response, "subscription")!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var list = JsonSerializer.Deserialize<List<MaxioSubscriptionEnvelope>>(response, JsonOptions) ?? new();
        return list.Select(s => s.Subscription!).Where(s => s is not null).ToList();
    }

    private static HttpContent JsonContent(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

    private async Task<JsonNode> GetAsync(string path, CancellationToken cancellationToken) =>
        await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    private async Task<JsonNode> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var httpResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)httpResponse.StatusCode;
        if (statusCode < 200 || statusCode >= 300)
        {
            throw new MaxioApiException(
                statusCode,
                ParseErrors(body),
                $"Maxio API call '{request.Method} {request.RequestUri}' returned {(int)httpResponse.StatusCode} {httpResponse.StatusCode}.");
        }
        return body.Length == 0 ? new JsonObject() : JsonNode.Parse(body) ?? new JsonObject();
    }

    private static T? ParseEnvelope<T>(JsonNode node, string envelopeKey) where T : class
    {
        var envelope = node[envelopeKey];
        return envelope == null ? null : envelope.Deserialize<T>(JsonOptions);
    }

    /// <summary>
    /// Parses the error model from the spec: {"errors": [ ... ]} — elements may be strings
    /// or single-property objects mapping a field to its message.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        var errors = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return errors;
            }
            var node = JsonNode.Parse(body);
            var errorsNode = node?["errors"];
            if (errorsNode is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is null) continue;
                    if (item is JsonObject obj && obj.Count > 0)
                    {
                        foreach (var (field, value) in obj)
                        {
                            errors.Add($"{field}: {value}");
                        }
                    }
                    else
                    {
                        errors.Add(item.ToJsonString());
                    }
                }
            }
            else if (errorsNode is JsonValue value)
            {
                errors.Add(value.ToJsonString());
            }
            else if (node is not null)
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

    private sealed record MaxioProductEnvelope(MaxioProduct? Product);

    private sealed record MaxioSubscriptionEnvelope(MaxioSubscription? Subscription);
}