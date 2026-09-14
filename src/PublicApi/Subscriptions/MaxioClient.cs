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

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _baseUri = options.Value.GetBaseUri();

        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<SiteResponse>("site.json", cancellationToken);
        return new MaxioSite(
            response!.Site.Currency,
            response.Site.RelationshipInvoicingEnabled,
            response.Site.Test);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var selector = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var responses = await GetAsync<List<ProductResponse>>(
            $"product_families/{selector}/products.json",
            cancellationToken);

        return (responses ?? new List<ProductResponse>())
            .Select(response => Map(response.Product))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetAsync<CustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        return response is null ? null : Map(response.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerPayload
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };
        var response = await PostAsync<CustomerResponse>("customers.json", request, cancellationToken);
        return Map(response.Customer);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<SubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        return response is null ? null : Map(response.Subscription);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequestContract
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            }
        };
        var response = await PostAsync<SubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return Map(response.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var responses = await GetAsync<List<SubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        return (responses ?? new List<SubscriptionResponse>())
            .Select(response => Map(response.Subscription))
            .ToList();
    }

    private async Task<T?> GetAsync<T>(
        string relativePath,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(relativePath));
        using var response = await SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadSuccessAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string relativePath, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(relativePath))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);
        return await ReadSuccessAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new MaxioApiException(null, "Maxio Advanced Billing could not be reached.", exception);
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private static async Task<MaxioApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ExtractErrors(body);
        return new MaxioApiException(
            response.StatusCode,
            string.IsNullOrWhiteSpace(message)
                ? $"Maxio returned HTTP {(int)response.StatusCode}."
                : message);
    }

    private static string? ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            var messages = new List<string>();
            AddErrorMessages(errors, messages);
            return messages.Count == 0 ? null : string.Join(" ", messages);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddErrorMessages(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddErrorMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddErrorMessages(property.Value, messages);
                }
                break;
        }
    }

    private Uri BuildUri(string relativePath) =>
        new($"{_baseUri.ToString().TrimEnd('/')}/{relativePath.TrimStart('/')}", UriKind.Absolute);

    private static MaxioCustomer Map(CustomerContract customer) => new(customer.Id, customer.Reference);

    private static MaxioProduct Map(ProductContract product) => new(
        product.Id,
        product.Handle ?? string.Empty,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard,
        product.ArchivedAt,
        product.ProductFamily.Handle);

    private static MaxioSubscription Map(SubscriptionContract subscription) => new(
        subscription.Id,
        subscription.State,
        subscription.ProductPriceInCents,
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt,
        subscription.Reference,
        subscription.Currency,
        Map(subscription.Customer),
        Map(subscription.Product));

    private sealed class ProductResponse
    {
        public ProductContract Product { get; init; } = null!;
    }

    private sealed class SiteResponse
    {
        public SiteContract Site { get; init; } = null!;
    }

    private sealed class SiteContract
    {
        public string Currency { get; init; } = string.Empty;
        [JsonPropertyName("relationship_invoicing_enabled")]
        public bool RelationshipInvoicingEnabled { get; init; }
        public bool Test { get; init; }
    }

    private sealed class CustomerResponse
    {
        public CustomerContract Customer { get; init; } = null!;
    }

    private sealed class SubscriptionResponse
    {
        public SubscriptionContract Subscription { get; init; } = null!;
    }

    private sealed class CustomerContract
    {
        public int Id { get; init; }
        public string? Reference { get; init; }
    }

    private sealed class ProductFamilyContract
    {
        public string Handle { get; init; } = string.Empty;
    }

    private sealed class ProductContract
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Handle { get; init; }
        public string? Description { get; init; }
        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; init; }
        public int Interval { get; init; }
        [JsonPropertyName("interval_unit")]
        public string IntervalUnit { get; init; } = string.Empty;
        [JsonPropertyName("require_credit_card")]
        public bool RequireCreditCard { get; init; }
        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; init; }
        [JsonPropertyName("product_family")]
        public ProductFamilyContract ProductFamily { get; init; } = null!;
    }

    private sealed class SubscriptionContract
    {
        public int Id { get; init; }
        public string State { get; init; } = string.Empty;
        [JsonPropertyName("product_price_in_cents")]
        public long ProductPriceInCents { get; init; }
        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
        [JsonPropertyName("next_assessment_at")]
        public DateTimeOffset? NextAssessmentAt { get; init; }
        public string? Reference { get; init; }
        public string Currency { get; init; } = string.Empty;
        public CustomerContract Customer { get; init; } = null!;
        public ProductContract Product { get; init; } = null!;
    }

    private sealed class CreateCustomerRequest
    {
        public CreateCustomerPayload Customer { get; init; } = null!;
    }

    private sealed class CreateCustomerPayload
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; init; } = string.Empty;
        [JsonPropertyName("last_name")]
        public string LastName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Reference { get; init; } = string.Empty;
    }

    private sealed class CreateSubscriptionRequestContract
    {
        public CreateSubscriptionPayload Subscription { get; init; } = null!;
    }

    private sealed class CreateSubscriptionPayload
    {
        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; init; } = string.Empty;
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; init; }
        public string Reference { get; init; } = string.Empty;
        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; init; } = string.Empty;
    }
}
