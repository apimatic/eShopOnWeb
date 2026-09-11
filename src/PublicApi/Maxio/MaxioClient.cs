using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioSettings settings)
    {
        var baseUrl = settings.ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioPlanDto>> ListPlansAsync(string productFamilyHandle)
    {
        var url = $"/product_families/handle:{productFamilyHandle}/products.json?per_page=200";
        _logger.LogDebug("Maxio: GET {Url}", url);

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<ProductItem>>(JsonOpts);
        return items?.ConvertAll(i => i.Product) ?? new List<MaxioPlanDto>();
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogDebug("Maxio: GET {Url}", url);

        var response = await _http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOpts);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCustomerCreateAttributes attributes)
    {
        var url = "/customers.json";
        _logger.LogDebug("Maxio: POST {Url} reference={Reference}", url, attributes.Reference);

        var payload = new { customer = attributes };
        var response = await _http.PostAsJsonAsync(url, payload, JsonOpts);

        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response);
            throw new MaxioApiException((int)response.StatusCode, errors);
        }

        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOpts);
        return wrapper!.Customer!;
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        MaxioCustomerCreateAttributes customerAttributes,
        bool customerAlreadyExists = false)
    {
        var url = "/subscriptions.json";
        _logger.LogDebug("Maxio: POST {Url} product={Product} reference={Reference}", url, productHandle, customerReference);

        object payload;
        if (customerAlreadyExists)
        {
            payload = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_reference = customerReference
                }
            };
        }
        else
        {
            payload = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_reference = customerReference,
                    customer_attributes = new
                    {
                        first_name = customerAttributes.FirstName,
                        last_name = customerAttributes.LastName,
                        email = customerAttributes.Email,
                        reference = customerAttributes.Reference
                    }
                }
            };
        }

        var response = await _http.PostAsJsonAsync(url, payload, JsonOpts);

        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response);
            throw new MaxioApiException((int)response.StatusCode, errors);
        }

        var wrapper = await response.Content.ReadFromJsonAsync<SubscriptionWrapper>(JsonOpts);
        return wrapper!.Subscription!;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"/customers/{customerId}/subscriptions.json?per_page=200";
        _logger.LogDebug("Maxio: GET {Url}", url);

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<SubscriptionItem>>(JsonOpts);
        return items?.ConvertAll(i => i.Subscription) ?? new List<MaxioSubscriptionDto>();
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array)
            {
                var errors = new List<string>();
                foreach (var el in errorsProp.EnumerateArray())
                    errors.Add(el.GetString() ?? string.Empty);
                return errors;
            }
            return new List<string> { body };
        }
        catch
        {
            return new List<string> { response.ReasonPhrase ?? "Unknown error" };
        }
    }

    private class ProductItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("product")]
        public MaxioPlanDto Product { get; set; } = new();
    }

    private class CustomerWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public MaxioCustomerDto? Customer { get; set; }
    }

    private class SubscriptionWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("subscription")]
        public MaxioSubscriptionDto? Subscription { get; set; }
    }

    private class SubscriptionItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("subscription")]
        public MaxioSubscriptionDto Subscription { get; set; } = new();
    }
}
