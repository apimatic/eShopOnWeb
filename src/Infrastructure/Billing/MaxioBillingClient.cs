using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingClient : IMaxioBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = Uri.EscapeDataString("handle:" + _productFamilyHandle);
        var response = await SendAsync<List<ProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json",
            null,
            "listProductsForProductFamily",
            cancellationToken);

        return response
            .Select(item => item.Product)
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new BillingPlan(
                product.Id,
                product.Handle!,
                product.Name,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit,
                product.RequireCreditCard))
            .OrderBy(product => product.PriceInCents)
            .ToArray();
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = "customers/lookup.json?reference=" + Uri.EscapeDataString(reference);
        var response = await SendOptionalAsync<CustomerEnvelope>(HttpMethod.Get, path, "readCustomerByReference", cancellationToken);
        return response is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            customer = new { first_name = firstName, last_name = lastName, email, reference }
        };
        var response = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", request, "createCustomer", cancellationToken);
        return MapCustomer(response.Customer);
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = "subscriptions/lookup.json?reference=" + Uri.EscapeDataString(reference);
        var response = await SendOptionalAsync<SubscriptionEnvelope>(HttpMethod.Get, path, "findSubscription", cancellationToken);
        return response is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                payment_collection_method = "remittance"
            }
        };
        var response = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, "createSubscription", cancellationToken);
        return MapSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            "listCustomerSubscriptions",
            cancellationToken);
        return response.Select(item => MapSubscription(item.Subscription)).ToArray();
    }

    private async Task<T?> SendOptionalAsync<T>(HttpMethod method, string path, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await SendHttpAsync(request, operation, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadSuccessAsync<T>(response, operation, cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? content,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, options: JsonOptions);
        }

        using var response = await SendHttpAsync(request, operation, cancellationToken);
        return await ReadSuccessAsync<T>(response, operation, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendHttpAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException(operation, null, new[] { "Maxio is unavailable." }, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(operation, null, new[] { "Maxio did not respond before the request timeout." }, exception);
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BillingProviderException(
                operation,
                (int)response.StatusCode,
                await ReadErrorsAsync(response, cancellationToken));
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new JsonException("The response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new BillingProviderException(operation, (int)response.StatusCode, new[] { "Maxio returned an unexpected response." }, exception);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new[] { response.ReasonPhrase ?? "Maxio rejected the request." };
            }

            var messages = new List<string>();
            CollectErrorMessages(errors, messages);
            return messages.Count == 0 ? new[] { response.ReasonPhrase ?? "Maxio rejected the request." } : messages;
        }
        catch (JsonException)
        {
            return new[] { response.ReasonPhrase ?? "Maxio rejected the request." };
        }
    }

    private static void CollectErrorMessages(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value)) messages.Add(value);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectErrorMessages(item, messages);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectErrorMessages(property.Value, messages);
                break;
        }
    }

    private static BillingCustomer MapCustomer(CustomerModel customer) =>
        new(customer.Id, customer.Reference ?? string.Empty, customer.Email);

    private static BillingSubscription MapSubscription(SubscriptionModel subscription) =>
        new(
            subscription.Id,
            subscription.Reference ?? string.Empty,
            subscription.Product.Handle ?? string.Empty,
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.Product.Interval,
            subscription.Product.IntervalUnit,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.Customer.Id,
            subscription.Product.ProductFamily?.Handle);

    private sealed class ProductEnvelope { public ProductModel Product { get; set; } = new(); }
    private sealed class CustomerEnvelope { public CustomerModel Customer { get; set; } = new(); }
    private sealed class SubscriptionEnvelope { public SubscriptionModel Subscription { get; set; } = new(); }

    private sealed class ProductModel
    {
        public long Id { get; set; }
        public string? Handle { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string IntervalUnit { get; set; } = string.Empty;
        public bool RequireCreditCard { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
        public ProductFamilyModel? ProductFamily { get; set; }
    }

    private sealed class ProductFamilyModel { public string? Handle { get; set; } }

    private sealed class CustomerModel
    {
        public long Id { get; set; }
        public string? Reference { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    private sealed class SubscriptionModel
    {
        public long Id { get; set; }
        public string State { get; set; } = string.Empty;
        public long ProductPriceInCents { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public string? Reference { get; set; }
        public CustomerModel Customer { get; set; } = new();
        public ProductModel Product { get; set; } = new();
    }
}
