using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Exception raised when the Maxio API returns a non-success response.
/// Message is built from the spec's error models: "errors" may be an array
/// (Error-List-Response), a map (Error-String-Map-Response / Customer-Error)
/// or a bare string (Single-String-Error-Response).
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public MaxioApiException(int statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxListPages = 10;
    private const int ListPageSize = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var baseUrl = _options.ResolveBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio:ApiKey is required (set it via the {MaxioOptions.ApiKeyEnvVar} environment variable or user-secrets).");
        }
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<TResult?> SendAsync<TResult>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, bool allowNotFound = false) where TResult : class
    {
        using var request = new HttpRequestMessage(method, path);
        ApplyAuth(request);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, SerializerOptions),
                Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }
            return JsonSerializer.Deserialize<TResult>(responseBody, SerializerOptions);
        }

        if (allowNotFound && (int)response.StatusCode == 404)
        {
            return null;
        }

        throw new MaxioApiException((int)response.StatusCode, responseBody,
            BuildErrorMessage((int)response.StatusCode, responseBody));
    }

    private static string BuildErrorMessage(int statusCode, string responseBody)
    {
        var details = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    details = string.Join("; ", errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()));
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    details = string.Join("; ", errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString())}"));
                }
                else if (errors.ValueKind == JsonValueKind.String)
                {
                    details = errors.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            details = responseBody;
        }

        return $"Maxio API returned HTTP {statusCode}.{(string.IsNullOrWhiteSpace(details) ? "" : " " + details)}";
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var path = $"products/handle/{UrlEncoder.Default.Encode(handle)}.json";
        var wrapper = await SendAsync<ProductWrapper>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Product;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxListPages; page++)
        {
            var path = $"products.json?page={page}&per_page={ListPageSize}";
            var wrappers = await SendAsync<List<ProductWrapper>>(HttpMethod.Get, path, null, cancellationToken);
            if (wrappers is null || wrappers.Count == 0)
            {
                break;
            }
            products.AddRange(wrappers.Select(w => w.Product));
            if (wrappers.Count < ListPageSize)
            {
                break;
            }
        }
        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={UrlEncoder.Default.Encode(reference)}";
        var wrapper = await SendAsync<CustomerWrapper>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Customer;
    }

    public Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateMaxioCustomerPayload { Customer = customer };
        return SendRequiredAsync<CustomerWrapper, MaxioCustomer>(HttpMethod.Post, "customers.json", body,
            cancellationToken, w => w.Customer);
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest subscription, CancellationToken cancellationToken = default)
    {
        return SendRequiredAsync<SubscriptionWrapper, MaxioSubscription>(HttpMethod.Post, "subscriptions.json",
            subscription, cancellationToken, w => w.Subscription);
    }

    public async Task<MaxioSubscription?> GetSubscriptionByIdAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/{subscriptionId}.json";
        var wrapper = await SendAsync<SubscriptionWrapper>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Subscription;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={UrlEncoder.Default.Encode(reference)}";
        var wrapper = await SendAsync<SubscriptionWrapper>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<SubscriptionWrapper>>(HttpMethod.Get, path, null, cancellationToken);
        return wrappers?.Select(w => w.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<TModel> SendRequiredAsync<TWrapper, TModel>(HttpMethod method, string path, object body,
        CancellationToken cancellationToken, Func<TWrapper, TModel?> unwrap) where TWrapper : class
    {
        var wrapper = await SendAsync<TWrapper>(method, path, body, cancellationToken);
        var model = wrapper is null ? default : unwrap(wrapper);
        if (model is null)
        {
            throw new MaxioApiException(200, string.Empty,
                $"Maxio API response for {method} {path} did not contain the expected payload.");
        }
        return model;
    }
}