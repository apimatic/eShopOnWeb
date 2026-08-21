using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing (formerly Chargify).
/// Auth: HTTP Basic with API key as username and literal "x" as password.
/// Host: Maxio:BaseUrl when set, otherwise https://{Maxio:Subdomain}.chargify.com/
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        var maxio = options.Value;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(maxio.GetApiBaseUrl(), UriKind.Absolute);
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null
            && !string.IsNullOrWhiteSpace(maxio.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxio.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public async Task<IReadOnlyList<MaxioProductInfo>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyId = productFamilyHandle.StartsWith("handle:", StringComparison.OrdinalIgnoreCase)
            ? productFamilyHandle
            : $"handle:{productFamilyHandle}";

        var products = new List<MaxioProductInfo>();
        const int perPage = 200;
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={perPage}";
            var payload = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken);
            if (payload is null || payload.Count == 0)
            {
                break;
            }

            foreach (var wrapper in payload)
            {
                var mapped = MapProduct(wrapper.Product);
                if (mapped is not null)
                {
                    products.Add(mapped);
                }
            }

            if (payload.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomerInfo?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken);
        return MapCustomer(payload?.Customer);
    }

    public async Task<MaxioCustomerInfo> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var payload = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json", body, cancellationToken);
        var customer = MapCustomer(payload?.Customer)
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty customer payload.");
        return customer;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var payload = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        if (payload is null)
        {
            return Array.Empty<MaxioSubscriptionInfo>();
        }

        return payload
            .Select(wrapper => MapSubscription(wrapper.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var payload = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", body, cancellationToken);
        return MapSubscription(payload?.Subscription)
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty subscription payload.");
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(relativePath, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<TResponse>(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, ReadErrorMessage(body, (int)response.StatusCode));
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    internal static string ReadErrorMessage(string? body, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio Advanced Billing returned HTTP {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors))
            {
                var parsed = FlattenErrors(errors);
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    return parsed;
                }
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? $"Maxio Advanced Billing returned HTTP {statusCode}.";
            }
        }
        catch (JsonException)
        {
            // Fall through to the status-code message rather than leaking a raw body.
        }

        return $"Maxio Advanced Billing returned HTTP {statusCode}.";
    }

    private static string FlattenErrors(JsonElement errors)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                return string.Join(" ", errors.EnumerateArray().Select(FormatElement).Where(s => s.Length > 0));
            case JsonValueKind.Object:
                return string.Join(" ", errors.EnumerateObject().Select(p =>
                {
                    var value = FlattenErrors(p.Value);
                    return string.IsNullOrWhiteSpace(value) ? p.Name : $"{p.Name}: {value}";
                }));
            case JsonValueKind.String:
                return errors.GetString() ?? string.Empty;
            default:
                return errors.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? string.Empty
                    : errors.ToString();
        }
    }

    private static string FormatElement(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : FlattenErrors(element);

    private static MaxioProductInfo? MapProduct(MaxioProduct? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
        {
            return null;
        }

        return new MaxioProductInfo(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit ?? "month",
            product.ProductFamily?.Handle);
    }

    private static MaxioCustomerInfo? MapCustomer(MaxioCustomer? customer)
    {
        if (customer is null || customer.Id == 0)
        {
            return null;
        }

        return new MaxioCustomerInfo(customer.Id, customer.Reference);
    }

    private static MaxioSubscriptionInfo? MapSubscription(MaxioSubscription? subscription)
    {
        if (subscription is null || subscription.Id == 0)
        {
            return null;
        }

        var productHandle = subscription.Product?.Handle ?? string.Empty;
        var productName = subscription.Product?.Name ?? productHandle;
        var price = subscription.ProductPriceInCents
                    ?? subscription.Product?.PriceInCents
                    ?? 0;

        return new MaxioSubscriptionInfo(
            subscription.Id,
            subscription.State ?? string.Empty,
            productHandle,
            productName,
            price,
            subscription.NextAssessmentAt);
    }
}
