using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API, written against the authoritative
/// Maxio OpenAPI specification (maxio-spec/openapi.yaml). Every endpoint, parameter and
/// payload shape used here comes from that spec:
///   - GET  /products.json                          (listProducts)
///   - GET  /customers/lookup.json?reference=...    (readCustomerByReference)
///   - POST /customers.json                         (createCustomer)
///   - GET  /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions)
///   - POST /subscriptions.json                     (createSubscription)
/// Authentication follows the spec's request example: HTTP Basic with the API key as the
/// username and the literal password "x".
/// </summary>
public sealed class MaxioApiClient
{
    private const int ProductsPerPage = 100;
    private const int MaxProductPages = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxio = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(maxio.ResolveBaseUrl() + "/");
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{maxio.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>List Products (GET /products.json), paging through the full result set.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        for (int page = 1; page <= MaxProductPages; page++)
        {
            var url = $"products.json?page={page}&per_page={ProductsPerPage}";
            var batch = await GetAsync<List<MaxioProductResponse>>(url, cancellationToken) ?? new List<MaxioProductResponse>();
            products.AddRange(batch.Select(w => w.Product));
            if (batch.Count < ProductsPerPage)
            {
                break;
            }
        }

        return products;
    }

    /// <summary>
    /// Read Customer by Reference (GET /customers/lookup.json?reference=...).
    /// Returns null when no customer matches the reference.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={HttpUtility.UrlEncode(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var wrapper = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return wrapper?.Customer;
    }

    /// <summary>Create Customer (POST /customers.json).</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var wrapper = await PostAsync<MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return wrapper.Customer;
    }

    /// <summary>List Customer Subscriptions (GET /customers/{customer_id}/subscriptions.json).</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        var batch = await GetAsync<List<MaxioSubscriptionResponse>>(url, cancellationToken) ?? new List<MaxioSubscriptionResponse>();
        return batch.Select(w => w.Subscription).ToList();
    }

    /// <summary>Create Subscription (POST /subscriptions.json).</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var wrapper = await PostAsync<MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return wrapper.Subscription;
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string url, object payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, ParseErrors(body));
    }

    /// <summary>
    /// Extracts human-readable error messages from the error response models defined in the
    /// Maxio spec: Error-List-Response ({ "errors": [ ... ] }), Error-String-Map-Response
    /// ({ "errors": { "field": "message" } }) and plain arrays of strings.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        errors.Add(element.ToString() ?? string.Empty);
                    }
                }
                else if (document.RootElement.ValueKind == JsonValueKind.Object &&
                         document.RootElement.TryGetProperty("errors", out var errorsElement))
                {
                    if (errorsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in errorsElement.EnumerateArray())
                        {
                            errors.Add(element.ToString() ?? string.Empty);
                        }
                    }
                    else if (errorsElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in errorsElement.EnumerateObject())
                        {
                            errors.Add($"{property.Name} {property.Value}");
                        }
                    }
                    else
                    {
                        errors.Add(errorsElement.ToString() ?? string.Empty);
                    }
                }
                else
                {
                    errors.Add(body);
                }
            }
            catch (JsonException)
            {
                errors.Add(body);
            }
        }

        if (errors.Count == 0)
        {
            errors.Add("Unknown Maxio API error.");
        }

        return errors;
    }
}
