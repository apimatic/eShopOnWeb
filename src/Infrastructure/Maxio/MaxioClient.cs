using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API, hand-written against the
/// authoritative OpenAPI specification in <c>maxio-spec/openapi.yaml</c>.
/// Only endpoints, parameters and schemas that exist in the spec are used.
/// Authentication is HTTP Basic with the API key as the username and a fixed
/// "x" password, per the spec description:
/// <c>curl -u &lt;api_key&gt;:x ... https://acme.chargify.com/subscriptions.json</c>.
/// </summary>
public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(int productFamilyId, CancellationToken cancellationToken = default);
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, string paymentCollectionMethod, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CancelSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        if (settings == null || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) is not configured. Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = ResolveBaseAddress(settings);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Base address per the spec's x-server-configuration: <c>https://{site}.chargify.com</c>
    /// for the US environment, <c>https://{site}.ebilling.maxio.com</c> for EU.
    /// <c>Maxio:BaseUrl</c>, when set, is used verbatim instead.
    /// </summary>
    private static Uri ResolveBaseAddress(MaxioOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        }

        var subdomain = settings.Subdomain!.Trim();
        var environment = (settings.Environment ?? "US").Trim().ToUpperInvariant();
        var host = environment switch
        {
            "EU" => $"https://{subdomain}.ebilling.maxio.com",
            _ => $"https://{subdomain}.chargify.com"
        };
        return new Uri(host + "/");
    }

    /// <summary>
    /// List Product Families — spec <c>GET /product_families.json</c> (operationId listProductFamilies).
    /// </summary>
    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var families = await GetJsonAsync<List<MaxioProductFamilyResponse>>("product_families.json", cancellationToken);
        return families!.Select(f => f.ProductFamily!).Where(f => f != null).ToList();
    }

    /// <summary>
    /// List Products in a Product Family — spec <c>GET /product_families/{id}/products.json</c>
    /// (operationId listProductFamilyProducts).
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        var products = await GetJsonAsync<List<MaxioProductResponse>>($"product_families/{productFamilyId}/products.json", cancellationToken);
        return products!.Select(p => p.Product!).Where(p => p != null).ToList();
    }

    /// <summary>
    /// Read Customer by Reference — spec <c>GET /customers/lookup.json?reference=</c>
    /// (operationId readCustomerByReference). Returns null when no customer matches (404).
    /// </summary>
    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        => ReadSingleAsync<MaxioCustomerResponse, MaxioCustomer>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            r => r.Customer, cancellationToken);

    /// <summary>
    /// Create Customer — spec <c>POST /customers.json</c> (operationId createCustomer).
    /// The <paramref name="reference"/> value must be unique per the spec, which makes
    /// customer creation idempotent for a given app user.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };
        var response = await PostJsonAsync<CreateMaxioCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return response!.Customer!;
    }

    /// <summary>
    /// Create Subscription — spec <c>POST /subscriptions.json</c> (operationId createSubscription),
    /// targeting an existing customer by <c>customer_id</c> and a product by <c>product_handle</c>.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, string paymentCollectionMethod, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };
        var response = await PostJsonAsync<CreateMaxioSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return response!.Subscription!;
    }

    /// <summary>
    /// List Customer Subscriptions — spec <c>GET /customers/{id}/subscriptions.json</c>
    /// (operationId listCustomerSubscriptions).
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetJsonAsync<List<MaxioSubscriptionResponse>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions!.Select(s => s.Subscription!).Where(s => s != null).ToList();
    }

    /// <summary>
    /// Find Subscription — spec <c>GET /subscriptions/lookup.json?reference=</c>
    /// (operationId findSubscription). Returns null when no subscription matches (404).
    /// </summary>
    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        => ReadSingleAsync<MaxioSubscriptionResponse, MaxioSubscription>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            r => r.Subscription, cancellationToken);

    /// <summary>
    /// Read Subscription — spec <c>GET /subscriptions/{subscription_id}.json</c>
    /// (operationId readSubscription). Returns null when not found (404).
    /// </summary>
    public Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => ReadSingleAsync<MaxioSubscriptionResponse, MaxioSubscription>($"subscriptions/{subscriptionId}.json",
            r => r.Subscription, cancellationToken);

    /// <summary>
    /// Cancel Subscription — spec <c>DELETE /subscriptions/{subscription_id}.json</c>
    /// (operationId cancelSubscription).
    /// </summary>
    public async Task<MaxioSubscription> CancelSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json");
        var response = await SendAsync(request, notFoundIsNull: false, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(_serializerOptions, cancellationToken);
        if (body?.Subscription == null)
        {
            return new MaxioSubscription { Id = subscriptionId, State = "canceled" };
        }
        return body.Subscription;
    }

    private async Task<TModel?> ReadSingleAsync<TResponse, TModel>(string url, Func<TResponse, TModel?> select, CancellationToken cancellationToken)
        where TResponse : class
    {
        var response = await GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
        var body = await response.Content.ReadFromJsonAsync<TResponse>(_serializerOptions, cancellationToken);
        return body == null ? default : select(body);
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        var response = await GetAsync(url, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(_serializerOptions, cancellationToken);
    }

    private Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return SendAsync(request, notFoundIsNull: true, cancellationToken);
    }

    private async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(string url, TRequest payload, CancellationToken cancellationToken)
        where TResponse : class
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: _serializerOptions)
        };
        var response = await SendAsync(request, notFoundIsNull: false, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(_serializerOptions, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool notFoundIsNull, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(0, ex.Message, new[] { $"Could not reach the Maxio API at '{_httpClient.BaseAddress}': {ex.Message}" });
        }

        if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return response;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException((int)response.StatusCode, body, ParseErrors(body));
        }

        return response;
    }

    /// <summary>
    /// Best-effort parse of the spec's error models: <c>{"errors": [...]}</c>,
    /// <c>{"errors": {"field": "message"}}</c>, or a plain-text body.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                return FlattenErrors(errors).ToList();
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return new[] { body.Length > 512 ? body[..512] : body };
    }

    private static IEnumerable<string> FlattenErrors(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var message in FlattenErrors(item))
                    {
                        yield return message;
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in FlattenErrors(property.Value))
                    {
                        yield return $"{property.Name}: {message}";
                    }
                }
                break;
        }
    }
}
