using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioProduct[]> GetProductsByFamilyAsync(string productFamilyHandle, CancellationToken ct = default)
    {
        var response = await _httpClient.GetFromJsonAsync<JsonElement[]>(
            $"products.json?product_family={productFamilyHandle}&per_page=200", ct);

        var products = new List<MaxioProduct>();
        foreach (var item in response)
        {
            if (item.TryGetProperty("product", out var productEl))
            {
                products.Add(productEl.Deserialize<MaxioProduct>(JsonOptions)!);
            }
        }
        return products.ToArray();
    }

    public async Task<MaxioProduct> GetProductByHandleAsync(string handle, CancellationToken ct = default)
    {
        var response = await _httpClient.GetFromJsonAsync<JsonElement>(
            $"products/handle/{handle}.json", ct);

        if (response.TryGetProperty("product", out var productEl))
        {
            return productEl.Deserialize<MaxioProduct>(JsonOptions)!;
        }
        throw new InvalidOperationException("Unexpected response from Maxio API");
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<JsonElement>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);

            if (response.TryGetProperty("customer", out var customerEl))
            {
                return customerEl.Deserialize<MaxioCustomer>(JsonOptions);
            }
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.NotFound ||
            ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["customer"] = new Dictionary<string, string?>
            {
                ["first_name"] = request.FirstName,
                ["last_name"] = request.LastName,
                ["email"] = request.Email,
                ["reference"] = request.Reference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (result.TryGetProperty("customer", out var customerEl))
        {
            return customerEl.Deserialize<MaxioCustomer>(JsonOptions)!;
        }
        throw new InvalidOperationException("Unexpected response from Maxio API");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct = default)
    {
        var subDict = new Dictionary<string, object?>();

        if (request.ProductHandle != null)
            subDict["product_handle"] = request.ProductHandle;
        if (request.ProductId.HasValue)
            subDict["product_id"] = request.ProductId.Value;
        if (request.CustomerId.HasValue)
            subDict["customer_id"] = request.CustomerId.Value;
        if (request.CustomerReference != null)
            subDict["customer_reference"] = request.CustomerReference;
        if (request.CustomerAttributes != null)
        {
            subDict["customer_attributes"] = new Dictionary<string, string?>
            {
                ["first_name"] = request.CustomerAttributes.FirstName,
                ["last_name"] = request.CustomerAttributes.LastName,
                ["email"] = request.CustomerAttributes.Email,
                ["reference"] = request.CustomerAttributes.Reference
            };
        }
        if (request.ProductPricePointId.HasValue)
            subDict["product_price_point_id"] = request.ProductPricePointId.Value;
        if (request.PaymentCollectionMethod != null)
            subDict["payment_collection_method"] = request.PaymentCollectionMethod;
        if (request.Reference != null)
            subDict["reference"] = request.Reference;

        var body = new Dictionary<string, object> { ["subscription"] = subDict };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (result.TryGetProperty("subscription", out var subEl))
        {
            return subEl.Deserialize<MaxioSubscription>(JsonOptions)!;
        }
        throw new InvalidOperationException("Unexpected response from Maxio API");
    }

    public async Task<MaxioSubscription[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetFromJsonAsync<JsonElement[]>(
            $"customers/{customerId}/subscriptions.json", ct);

        if (response == null)
            return Array.Empty<MaxioSubscription>();

        var subscriptions = new List<MaxioSubscription>();
        foreach (var item in response)
        {
            if (item.TryGetProperty("subscription", out var subEl))
            {
                subscriptions.Add(subEl.Deserialize<MaxioSubscription>(JsonOptions)!);
            }
        }
        return subscriptions.ToArray();
    }

    public async Task<MaxioSubscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetFromJsonAsync<JsonElement>(
            $"subscriptions/{subscriptionId}.json", ct);

        if (response.TryGetProperty("subscription", out var subEl))
        {
            return subEl.Deserialize<MaxioSubscription>(JsonOptions)!;
        }
        throw new InvalidOperationException("Unexpected response from Maxio API");
    }
}
