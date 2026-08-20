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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly SemaphoreSlim ConcurrencyGate = new(4, 4);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient, MaxioOptions options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = options.GetBaseUri();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var family = $"handle:{Uri.EscapeDataString(productFamilyHandle)}";
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"product_families/{family}/products.json?per_page=200"),
            cancellationToken);
        var items = await ReadRequiredAsync<List<ProductEnvelope>>(response, cancellationToken);

        return items
            .Where(item => item.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(item.Product.Handle))
            .Select(item => item.Product.ToPlan())
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "site.json"),
            cancellationToken);
        var envelope = await ReadRequiredAsync<SiteEnvelope>(response, cancellationToken);
        return new MaxioSite(envelope.Site.RelationshipInvoicingEnabled, envelope.Site.Test);
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        var envelope = await ReadRequiredAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope.Customer.ToCustomer();
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomer customer,
        CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CustomerRequest
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            },
            UniquenessToken = customer.UniquenessToken
        };
        var response = await SendAsync(
            () => CreateJsonRequest(HttpMethod.Post, "customers.json", request),
            cancellationToken);
        var envelope = await ReadRequiredAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope.Customer.ToCustomer();
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        var envelope = await ReadRequiredAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription.ToSubscription();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new SubscriptionRequest
            {
                ProductHandle = subscription.ProductHandle,
                CustomerReference = subscription.CustomerReference,
                Reference = subscription.SubscriptionReference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            },
            UniquenessToken = subscription.UniquenessToken
        };
        var response = await SendAsync(
            () => CreateJsonRequest(HttpMethod.Post, "subscriptions.json", request),
            cancellationToken);
        var envelope = await ReadRequiredAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription.ToSubscription();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/{customerId}/subscriptions.json"),
            cancellationToken);
        var items = await ReadRequiredAsync<List<SubscriptionEnvelope>>(response, cancellationToken);
        return items
            .Where(item => item.Subscription.Customer is not null &&
                           !string.IsNullOrWhiteSpace(item.Subscription.Product?.Handle))
            .Select(item => item.Subscription.ToSubscription())
            .ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            await ConcurrencyGate.WaitAsync(cancellationToken);
            HttpResponseMessage response;
            try
            {
                using var request = requestFactory();
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            finally
            {
                ConcurrencyGate.Release();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2 &&
                response.Headers.RetryAfter?.Delta is { } retryDelay && retryDelay <= TimeSpan.FromSeconds(30))
            {
                response.Dispose();
                await Task.Delay(retryDelay, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            {
                return response;
            }

            var message = await ReadErrorAsync(response, cancellationToken);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new MaxioApiException(statusCode, message);
        }
    }

    private static HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string uri, T content)
    {
        return new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(content, options: SerializerOptions)
        };
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
                return value ?? throw new MaxioApiException(
                    HttpStatusCode.BadGateway,
                    "Maxio returned an empty or invalid response.");
            }
            catch (JsonException exception)
            {
                throw new MaxioApiException(
                    HttpStatusCode.BadGateway,
                    "Maxio returned an empty or invalid response.",
                    exception);
            }
        }
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 4096)
        {
            body = body[..4096];
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return string.Join("; ", errors.EnumerateArray().Select(item => item.ToString()));
                }

                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to a bounded generic message; upstream HTML is not exposed.
        }

        return $"Maxio request failed with HTTP {(int)response.StatusCode}.";
    }

    private sealed class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public required ProductWire Product { get; init; }
    }

    private sealed class CustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public required CustomerWire Customer { get; init; }
    }

    private sealed class SiteEnvelope
    {
        [JsonPropertyName("site")]
        public required SiteWire Site { get; init; }
    }

    private sealed class SiteWire
    {
        [JsonPropertyName("relationship_invoicing_enabled")]
        public bool RelationshipInvoicingEnabled { get; init; }
        [JsonPropertyName("test")]
        public bool Test { get; init; }
    }

    private sealed class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public required SubscriptionWire Subscription { get; init; }
    }

    private sealed class ProductWire
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
        [JsonPropertyName("handle")]
        public string? Handle { get; init; }
        [JsonPropertyName("description")]
        public string? Description { get; init; }
        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; init; }
        [JsonPropertyName("interval")]
        public int Interval { get; init; }
        [JsonPropertyName("interval_unit")]
        public string IntervalUnit { get; init; } = string.Empty;
        [JsonPropertyName("require_credit_card")]
        public bool RequireCreditCard { get; init; }
        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; init; }

        public MaxioPlan ToPlan() => new(
            Id, Name, Handle!, Description, PriceInCents, Interval, IntervalUnit, RequireCreditCard);
    }

    private sealed class CustomerWire
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }
        [JsonPropertyName("reference")]
        public string? Reference { get; init; }
        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;

        public MaxioCustomer ToCustomer() => new(Id, Reference ?? string.Empty, Email);
    }

    private sealed class SubscriptionWire
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }
        [JsonPropertyName("reference")]
        public string? Reference { get; init; }
        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;
        [JsonPropertyName("product_price_in_cents")]
        public long ProductPriceInCents { get; init; }
        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
        [JsonPropertyName("customer")]
        public CustomerWire? Customer { get; init; }
        [JsonPropertyName("product")]
        public ProductWire? Product { get; init; }

        public MaxioSubscription ToSubscription()
        {
            if (Customer is null || Product is null || string.IsNullOrWhiteSpace(Product.Handle))
            {
                throw new MaxioApiException(
                    HttpStatusCode.BadGateway,
                    "Maxio returned a subscription without its customer or product.");
            }

            return new MaxioSubscription(
                Id,
                Reference ?? string.Empty,
                State,
                ProductPriceInCents,
                CurrentPeriodEndsAt,
                Customer.Id,
                Customer.Reference ?? string.Empty,
                Product.Id,
                Product.Name,
                Product.Handle,
                Product.Interval,
                Product.IntervalUnit);
        }
    }

    private sealed class CreateCustomerRequest
    {
        [JsonPropertyName("customer")]
        public required CustomerRequest Customer { get; init; }
        [JsonPropertyName("uniqueness_token")]
        public Guid UniquenessToken { get; init; }
    }

    private sealed class CustomerRequest
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; init; } = string.Empty;
        [JsonPropertyName("last_name")]
        public string LastName { get; init; } = string.Empty;
        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;
        [JsonPropertyName("reference")]
        public string Reference { get; init; } = string.Empty;
    }

    private sealed class CreateSubscriptionRequest
    {
        [JsonPropertyName("subscription")]
        public required SubscriptionRequest Subscription { get; init; }
        [JsonPropertyName("uniqueness_token")]
        public Guid UniquenessToken { get; init; }
    }

    private sealed class SubscriptionRequest
    {
        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; init; } = string.Empty;
        [JsonPropertyName("customer_reference")]
        public string CustomerReference { get; init; } = string.Empty;
        [JsonPropertyName("reference")]
        public string Reference { get; init; } = string.Empty;
        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; init; } = string.Empty;
    }
}
