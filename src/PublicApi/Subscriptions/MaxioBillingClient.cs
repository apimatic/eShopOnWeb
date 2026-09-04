using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        _options.ValidateCredentials();
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var responses = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json",
            null,
            cancellationToken);

        return responses.Select(response => response.Product).OfType<MaxioProduct>().ToList();
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        SendNullableAsync<MaxioCustomerResponse, MaxioCustomer>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            response => response.Customer);

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            },
            cancellationToken);

        return response.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no customer.");
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        SendNullableAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            response => response.Subscription);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    // The seeded plans do not require a payment profile. Invoice
                    // collection is the contract-supported no-card signup mode.
                    PaymentCollectionMethod = "invoice"
                }
            },
            cancellationToken);

        return response.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no subscription.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return responses.Select(response => response.Subscription).OfType<MaxioSubscription>().ToList();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        _options.ValidateCredentials();

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x")));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, ExtractError(content));
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an invalid response.", exception);
        }
    }

    private async Task<TValue?> SendNullableAsync<TResponse, TValue>(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        Func<TResponse, TValue?> selector)
    {
        try
        {
            var response = await SendAsync<TResponse>(method, path, null, cancellationToken);
            return selector(response);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private static string ExtractError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return string.Join("; ", errors.EnumerateArray().Select(error => error.GetString()).Where(message => !string.IsNullOrWhiteSpace(message)));
                }

                if (errors.ValueKind == JsonValueKind.Object)
                {
                    return string.Join("; ", errors.EnumerateObject().Select(property => $"{property.Name}: {property.Value}"));
                }
            }
        }
        catch (JsonException)
        {
            // Preserve the upstream status even when an intermediary returned non-JSON.
        }

        return "The Maxio request failed.";
    }
}

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public sealed class MaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer? Customer { get; set; }
}

public sealed class MaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription? Subscription { get; set; }
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_reference")] public string CustomerReference { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = string.Empty;
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("product_price_point_id")] public int ProductPricePointId { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}
