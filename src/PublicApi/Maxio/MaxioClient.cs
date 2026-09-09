using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing (Billing API) REST API.
/// Authentication is HTTP Basic over TLS: the API key as username, the literal "x" as password.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;

        var baseUrl = !string.IsNullOrWhiteSpace(maxioOptions.BaseUrl)
            ? maxioOptions.BaseUrl.TrimEnd('/')
            : $"https://{maxioOptions.Subdomain}.chargify.com";

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var wrappers = await GetAsync<List<MaxioProductWrapper>>(
                $"product_families/{Uri.EscapeDataString($"handle:{productFamilyHandle}")}/products.json?page={page}&per_page={perPage}",
                cancellationToken);

            products.AddRange(wrappers.Select(w => w.Product));

            if (wrappers.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var (statusCode, body) = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", requestBody: null, cancellationToken);

            var wrapper = Deserialize<MaxioCustomerWrapper>(body, statusCode);
            return wrapper.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            // No customer exists yet for this reference.
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var wrapper = await PostAsync<MaxioCustomerWrapper>("customers.json", request, cancellationToken);
        return wrapper.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var wrapper = await PostAsync<MaxioSubscriptionWrapper>("subscriptions.json", request, cancellationToken);
        return wrapper.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var wrappers = await GetAsync<List<MaxioSubscriptionWrapper>>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={perPage}",
                cancellationToken);

            subscriptions.AddRange(wrappers.Select(w => w.Subscription));

            if (wrappers.Count < perPage)
            {
                break;
            }

            page++;
        }

        return subscriptions;
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var (statusCode, body) = await SendAsync(HttpMethod.Get, relativeUrl, requestBody: null, cancellationToken);
        return Deserialize<T>(body, statusCode);
    }

    private async Task<T> PostAsync<T>(string relativeUrl, object requestBody, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody, requestBody.GetType(), _serializerOptions);
        var (statusCode, body) = await SendAsync(HttpMethod.Post, relativeUrl, json, cancellationToken);
        return Deserialize<T>(body, statusCode);
    }

    private async Task<(System.Net.HttpStatusCode StatusCode, string Body)> SendAsync(HttpMethod method, string relativeUrl, string? requestBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (requestBody != null)
        {
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(
                (int)response.StatusCode,
                body,
                $"Maxio API call {method} {relativeUrl} failed with status {(int)response.StatusCode}: {Truncate(body, 512)}");
        }

        return (response.StatusCode, body);
    }

    private T Deserialize<T>(string body, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(body, _serializerOptions);
            return result ?? throw new MaxioApiException((int)statusCode, body, "Maxio API returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException((int)statusCode, body, $"Maxio API returned an unexpected response: {ex.Message}");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "...";
}
