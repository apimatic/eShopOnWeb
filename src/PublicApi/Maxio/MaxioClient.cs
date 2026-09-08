using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client over the Maxio Advanced Billing API. Every call maps to an operation that
/// exists in the Maxio OpenAPI specification (<c>maxio-spec/openapi.yaml</c>) — paths, query
/// parameters, payloads and error handling all follow that contract. This client does not add
/// domain behavior; see <see cref="MaxioSubscriptionService"/> for orchestration.
/// </summary>
public class MaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var resolved = options.Value.ResolveBaseUri();
        if (!Uri.Equals(_httpClient.BaseAddress, resolved))
        {
            _httpClient.BaseAddress = resolved;
        }

        var apiKey = options.Value.RequireApiKey();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{apiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// List products (plans) for a product family.
    /// GET /product_families/{product_family_id}/products.json
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var products = await SendAsync<IReadOnlyList<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken);

        var result = new List<MaxioProduct>(products?.Count ?? 0);
        if (products is not null)
        {
            foreach (var envelope in products)
            {
                if (envelope.Product is not null)
                {
                    result.Add(envelope.Product);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Read a product by its API handle.
    /// GET /products/handle/{api_handle}.json
    /// </summary>
    public async Task<MaxioProduct?> GetProductByHandleAsync(
        string productHandle, CancellationToken cancellationToken = default)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        return await SendAsync<ProductResponse>(HttpMethod.Get, path, null, cancellationToken) is { } response
            ? response.Product
            : null;
    }

    /// <summary>
    /// Read a customer by its unique reference.
    /// GET /customers/lookup.json?reference=...
    /// Returns <c>null</c> when no customer matches (HTTP 404).
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            return await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken) is { } response
                ? response.Customer
                : null;
        }
        catch (MaxioApiException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Create a customer.
    /// POST /customers.json
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest(customer);
        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, string.Empty, "Create customer returned an empty payload.");
    }

    /// <summary>
    /// List the subscriptions that belong to a customer.
    /// GET /customers/{customer_id}/subscriptions.json
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var subscriptions = await SendAsync<IReadOnlyList<SubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);

        var result = new List<MaxioSubscription>(subscriptions?.Count ?? 0);
        if (subscriptions is not null)
        {
            foreach (var envelope in subscriptions)
            {
                if (envelope.Subscription is not null)
                {
                    result.Add(envelope.Subscription);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Create a subscription for an existing customer and product.
    /// POST /subscriptions.json
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionAttributes subscription, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest(subscription);
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, string.Empty, "Create subscription returned an empty payload.");
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method, string pathAndQuery, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, pathAndQuery);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.RequestTimeout, string.Empty, "The Maxio Advanced Billing request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, string.Empty,
                $"The Maxio Advanced Billing request failed at the transport level: {ex.Message}");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    return default;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Maxio Advanced Billing returned an unparseable response for {Method} {Path}.", method, pathAndQuery);
                    throw new MaxioApiException(response.StatusCode, responseBody,
                        $"Maxio Advanced Billing returned an unparseable response ({response.StatusCode}).");
                }
            }

            var message = ErrorMessage(response.StatusCode, responseBody);
            _logger.LogWarning("Maxio Advanced Billing request {Method} {Path} failed with {StatusCode}: {Message}",
                method, pathAndQuery, response.StatusCode, message);
            throw new MaxioApiException(response.StatusCode, responseBody, message);
        }
    }

    private static string ErrorMessage(HttpStatusCode statusCode, string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return $"The Maxio Advanced Billing request failed with HTTP {(int)statusCode} ({statusCode}).";
        }

        var prefix = $"The Maxio Advanced Billing request failed with HTTP {(int)statusCode} ({statusCode}).";

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var details = FormatErrors(errors);
                if (details.Length > 0)
                {
                    return $"{prefix} {details}";
                }
            }
        }
        catch (JsonException)
        {
            // Fall through; the raw body is kept in MaxioApiException.ResponseBody.
        }

        return prefix;
    }

    private static string FormatErrors(JsonElement errors)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    switch (item.ValueKind)
                    {
                        case JsonValueKind.String when !string.IsNullOrWhiteSpace(item.GetString()):
                            parts.Add(item.GetString()!);
                            break;
                        case JsonValueKind.Object:
                            foreach (var property in item.EnumerateObject())
                            {
                                if (property.Value.ValueKind == JsonValueKind.String &&
                                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                                {
                                    parts.Add($"{property.Name}: {property.Value.GetString()}");
                                }
                            }
                            break;
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : string.Empty;
            }
            case JsonValueKind.Object:
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        parts.Add($"{property.Name}: {property.Value.GetString()}");
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : string.Empty;
            }
            default:
                return errors.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(errors.GetString())
                    ? errors.GetString()!
                    : string.Empty;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return options;
    }
}
