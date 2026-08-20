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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioConcurrencyLimiter _limiter;
    private readonly MaxioOptions _options;
    private readonly Uri _baseUri;

    public MaxioBillingGateway(HttpClient httpClient, IOptions<MaxioOptions> options,
        MaxioConcurrencyLimiter limiter)
    {
        _httpClient = httpClient;
        _limiter = limiter;
        _options = options.Value;
        _baseUri = new Uri(string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com"
            : _options.BaseUrl, UriKind.Absolute);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/{family}/products.json?per_page=200", null, cancellationToken);
        var products = await DeserializeAsync<List<ProductResponse>>(response, cancellationToken) ?? new();

        return products.Where(x => x.Product.ArchivedAt is null)
            .Select(x => new BillingPlan(x.Product.Id, x.Product.Handle, x.Product.Name,
                x.Product.Description ?? string.Empty, x.Product.PriceInCents, x.Product.Interval,
                x.Product.IntervalUnit))
            .OrderBy(x => x.PriceInCents)
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken,
            allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var result = await DeserializeAsync<CustomerResponse>(response, cancellationToken);
        return result is null ? null : new BillingCustomer(result.Customer.Id, result.Customer.Reference ?? reference);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(BillingUser user, string reference,
        CancellationToken cancellationToken = default)
    {
        var body = new CustomerCreateRequest(new CustomerCreate(user.FirstName, user.LastName, user.Email, reference));
        var response = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        var result = await DeserializeAsync<CustomerResponse>(response, cancellationToken)
            ?? throw InvalidResponse("Maxio returned an empty customer response.");
        return new BillingCustomer(result.Customer.Id, result.Customer.Reference ?? reference);
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null,
            cancellationToken);
        var result = await DeserializeAsync<List<SubscriptionResponse>>(response, cancellationToken) ?? new();
        return result.Select(MapSubscription).ToList();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string productHandle,
        string reference, CancellationToken cancellationToken = default)
    {
        var body = new SubscriptionCreateRequest(new SubscriptionCreate(productHandle, customerId, reference));
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var result = await DeserializeAsync<SubscriptionResponse>(response, cancellationToken)
            ?? throw InvalidResponse("Maxio returned an empty subscription response.");
        return MapSubscription(result);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, object? body,
        CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var attempts = method == HttpMethod.Get ? 3 : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, BuildUri(relativePath));
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

            HttpResponseMessage response;
            try
            {
                using var lease = await _limiter.EnterAsync(cancellationToken);
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MaxioApiException(HttpStatusCode.GatewayTimeout,
                    "Maxio did not respond before the request timeout.", true, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway,
                    "Maxio could not be reached.", true, ex);
            }

            if (response.IsSuccessStatusCode || allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return response;

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests ||
                            (int)response.StatusCode >= 500;
            if (transient && attempt < attempts)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), cancellationToken);
                continue;
            }

            var message = await ReadErrorAsync(response, cancellationToken);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new MaxioApiException(statusCode, message, transient);
        }

        throw InvalidResponse("Maxio request failed.");
    }

    private Uri BuildUri(string relativePath) =>
        new($"{_baseUri.AbsoluteUri.TrimEnd('/')}/{relativePath.TrimStart('/')}", UriKind.Absolute);

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway,
                    "Maxio returned an unexpected response.", false, ex);
            }
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (content.Length > 4096) content = content[..4096];
        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind == JsonValueKind.Array
                    ? string.Join("; ", errors.EnumerateArray().Select(x => x.ToString()))
                    : errors.ToString();
            }
        }
        catch (JsonException) { }

        return string.IsNullOrWhiteSpace(content)
            ? $"Maxio returned HTTP {(int)response.StatusCode}."
            : content;
    }

    private static BillingSubscription MapSubscription(SubscriptionResponse response)
    {
        var value = response.Subscription;
        var product = value.Product ?? throw InvalidResponse("Maxio subscription has no product.");
        return new BillingSubscription(value.Id, value.Customer?.Id ?? 0, value.Reference, value.State,
            product.Handle, product.Name, value.ProductPriceInCents ?? product.PriceInCents, product.Interval,
            product.IntervalUnit, value.NextAssessmentAt, product.ProductFamily?.Handle ?? string.Empty);
    }

    private static MaxioApiException InvalidResponse(string message) =>
        new(HttpStatusCode.BadGateway, message);

    private sealed record ProductResponse([property: JsonPropertyName("product")] Product Product);
    private sealed record CustomerResponse([property: JsonPropertyName("customer")] Customer Customer);
    private sealed record SubscriptionResponse([property: JsonPropertyName("subscription")] Subscription Subscription);
    private sealed record CustomerCreateRequest([property: JsonPropertyName("customer")] CustomerCreate Customer);
    private sealed record SubscriptionCreateRequest(
        [property: JsonPropertyName("subscription")] SubscriptionCreate Subscription);

    private sealed record CustomerCreate(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record SubscriptionCreate(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod = "remittance");

    private sealed class Customer
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("reference")] public string? Reference { get; init; }
    }

    private sealed class Subscription
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
        [JsonPropertyName("reference")] public string? Reference { get; init; }
        [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; init; }
        [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
        [JsonPropertyName("customer")] public Customer? Customer { get; init; }
        [JsonPropertyName("product")] public Product? Product { get; init; }
    }

    private sealed class Product
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("handle")] public string Handle { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
        [JsonPropertyName("interval")] public int Interval { get; init; }
        [JsonPropertyName("interval_unit")] public string IntervalUnit { get; init; } = string.Empty;
        [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
        [JsonPropertyName("product_family")] public ProductFamily? ProductFamily { get; init; }
    }

    private sealed class ProductFamily
    {
        [JsonPropertyName("handle")] public string Handle { get; init; } = string.Empty;
    }
}
