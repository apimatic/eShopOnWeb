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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly object _configurationLock = new();
    private bool _isConfigured;

    public MaxioBillingGateway(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            var products = await GetRequiredAsync<List<ProductEnvelope>>(
                $"products.json?page={page}&per_page={PageSize}", cancellationToken);

            plans.AddRange(products
                .Select(item => item.Product)
                .Where(IsConfiguredPlan)
                .Select(MapPlan));

            if (products.Count < PageSize)
            {
                break;
            }
        }

        return plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name).ToList();
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var encodedHandle = Uri.EscapeDataString(productHandle);
        var response = await GetOptionalAsync<ProductEnvelope>(
            $"products/handle/{encodedHandle}.json", cancellationToken);

        return response is not null && IsConfiguredPlan(response.Product)
            ? MapPlan(response.Product)
            : null;
    }

    public async Task<BillingCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var encodedReference = Uri.EscapeDataString(reference);
        var response = await GetOptionalAsync<CustomerEnvelope>(
            $"customers/lookup.json?reference={encodedReference}", cancellationToken);

        return response is null ? null : new BillingCustomer(response.Customer.Id, response.Customer.Reference);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        BillingCustomerIdentity identity,
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new CreateCustomerEnvelope(new CreateCustomer(
            identity.FirstName,
            identity.LastName,
            identity.Email,
            reference));
        var response = await SendRequiredAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", request, cancellationToken);

        return new BillingCustomer(response.Customer.Id, response.Customer.Reference);
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var encodedReference = Uri.EscapeDataString(reference);
        var response = await GetOptionalAsync<SubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={encodedReference}", cancellationToken);

        return response is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new CreateSubscriptionEnvelope(new CreateSubscription(
            productHandle,
            customerId,
            reference,
            "remittance"));
        var response = await SendRequiredAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        return MapSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await GetRequiredAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        return response
            .Where(item => item.Subscription.Product is not null && IsConfiguredPlan(item.Subscription.Product))
            .Select(item => MapSubscription(item.Subscription))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }

    private void EnsureConfigured()
    {
        if (_isConfigured)
        {
            return;
        }

        lock (_configurationLock)
        {
            if (_isConfigured)
            {
                return;
            }

            try
            {
                _options.Validate();
                _httpClient.BaseAddress = _options.GetBaseAddress();
                _httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);
                _isConfigured = true;
            }
            catch (InvalidOperationException exception)
            {
                throw new BillingProviderException("Maxio configuration is incomplete or invalid.", exception);
            }
        }
    }

    private bool IsConfiguredPlan(MaxioProduct product) =>
        product.ArchivedAt is null &&
        string.Equals(
            product.ProductFamily?.Handle,
            _options.ProductFamilyHandle,
            StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        product.Handle,
        product.Name,
        product.Description ?? string.Empty,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit);

    private static BillingSubscription MapSubscription(MaxioSubscription subscription)
    {
        if (subscription.Product is null)
        {
            throw new BillingProviderException("Maxio returned a subscription without a plan.");
        }

        return new BillingSubscription(
            subscription.Id,
            subscription.Customer.Id,
            subscription.Product.Handle,
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.Product.Interval,
            subscription.Product.IntervalUnit,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }

    private Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken) =>
        SendRequiredAsync<T>(HttpMethod.Get, path, null, cancellationToken);

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendRequiredAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException("Maxio could not be reached.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("The Maxio request timed out.", exception);
        }
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BillingProviderException(
                $"Maxio request failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new BillingProviderException("Maxio returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new BillingProviderException("Maxio returned an unexpected response shape.", exception);
        }
    }

    private sealed record ProductEnvelope([property: JsonPropertyName("product")] MaxioProduct Product);
    private sealed record CustomerEnvelope([property: JsonPropertyName("customer")] MaxioCustomer Customer);
    private sealed record SubscriptionEnvelope([property: JsonPropertyName("subscription")] MaxioSubscription Subscription);
    private sealed record CreateCustomerEnvelope([property: JsonPropertyName("customer")] CreateCustomer Customer);
    private sealed record CreateSubscriptionEnvelope([property: JsonPropertyName("subscription")] CreateSubscription Subscription);

    private sealed record CreateCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record CreateSubscription(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

    private sealed class MaxioCustomer
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("reference")]
        public string Reference { get; init; } = string.Empty;
    }

    private sealed class MaxioProduct
    {
        [JsonPropertyName("handle")]
        public string Handle { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; init; }

        [JsonPropertyName("interval")]
        public int Interval { get; init; }

        [JsonPropertyName("interval_unit")]
        public string IntervalUnit { get; init; } = string.Empty;

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; init; }

        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; init; }
    }

    private sealed class MaxioProductFamily
    {
        [JsonPropertyName("handle")]
        public string Handle { get; init; } = string.Empty;
    }

    private sealed class MaxioSubscription
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;

        [JsonPropertyName("product_price_in_cents")]
        public long ProductPriceInCents { get; init; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("customer")]
        public MaxioCustomer Customer { get; init; } = new();

        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; init; }
    }
}
