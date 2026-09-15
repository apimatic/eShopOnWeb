using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken);
}

/// <summary>
/// Small, deliberately explicit adapter over the documented Maxio Billing API endpoints.
/// Maxio models are private to this boundary so external JSON does not leak into API contracts.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var response = await GetAsync<List<MaxioProductEnvelope>>($"product_families/{family}/products.json?per_page=200", cancellationToken);
        return response?.Where(item => item.Product is not null).Select(item => item.Product!).ToList()
            ?? (IReadOnlyList<MaxioProduct>)Array.Empty<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken, returnNullForNotFound: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        // ApplicationUser has no given-name fields. These names satisfy Maxio's required fields;
        // the stable application user id is the authoritative customer reference.
        var localPart = email.Split('@', 2)[0];
        var request = new
        {
            customer = new
            {
                first_name = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart,
                last_name = "Customer",
                email,
                reference
            }
        };

        var response = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a customer response without a customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return response?.Where(item => item.Subscription is not null).Select(item => item.Subscription!).ToList()
            ?? (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var request = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                // Remittance is the documented non-automatic collection method. It permits
                // signup without collecting a card; Maxio issues invoices for later payment.
                payment_collection_method = "remittance"
            }
        };

        var response = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a subscription response without a subscription.");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken, bool returnNullForNotFound = false)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (returnNullForNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await DeserializeResponse<T>(response, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await DeserializeResponse<T>(response, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<T?> DeserializeResponse<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, $"Maxio returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

public sealed class MaxioProduct
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioProductFamily
{
    public string Handle { get; init; } = string.Empty;
}

public sealed class MaxioCustomer
{
    public long Id { get; init; }
    public string? Reference { get; init; }
}

public sealed class MaxioSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public MaxioProduct? Product { get; init; }
}
