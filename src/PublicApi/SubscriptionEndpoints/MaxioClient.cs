using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Small, deliberately contract-focused client for Maxio Advanced Billing's HTTP API.</summary>
public sealed class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _productFamilyHandle = settings.ProductFamilyHandle;
        _httpClient.BaseAddress = settings.GetBaseUri();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        // Product-family handles are accepted by Maxio when prefixed with "handle:".
        var family = Uri.EscapeDataString("handle:" + _productFamilyHandle);
        using var document = await SendAsync(HttpMethod.Get, $"product_families/{family}/products.json", null, "plan listing", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(item => ReadPlan(item.GetProperty("product")))
            .Where(plan => plan is not null)
            .Cast<MaxioPlan>()
            .ToList();
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var body = new { customer = new { reference, email, first_name = firstName, last_name = lastName } };
        using var createResponse = await SendRawAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (createResponse.StatusCode == HttpStatusCode.Created)
        {
            using var document = await ReadDocumentAsync(createResponse, cancellationToken);
            return new MaxioCustomer(document.RootElement.GetProperty("customer").GetProperty("id").GetInt64());
        }

        // The reference is unique in Maxio. A simultaneous first request may have created it.
        if (createResponse.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return await FindCustomerAsync(reference, cancellationToken)
                ?? throw new MaxioApiException((int)createResponse.StatusCode, "customer creation");
        }

        throw new MaxioApiException((int)createResponse.StatusCode, "customer creation");
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var lookup = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendRawAsync(HttpMethod.Get, lookup, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new MaxioApiException((int)response.StatusCode, "customer lookup");
        }

        using var document = await ReadDocumentAsync(response, cancellationToken);
        return new MaxioCustomer(document.RootElement.GetProperty("customer").GetProperty("id").GetInt64());
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, "subscription lookup", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(item => ReadSubscription(item.GetProperty("subscription")))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                reference,
                // The seeded catalog intentionally permits enrollment without a stored payment method.
                // Maxio's documented remittance collection method creates that invoice-based subscription.
                payment_collection_method = "remittance"
            }
        };

        using var document = await SendAsync(HttpMethod.Post, "subscriptions.json", body, "subscription creation", cancellationToken, HttpStatusCode.Created);
        return ReadSubscription(document.RootElement.GetProperty("subscription"));
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string operation, CancellationToken cancellationToken, HttpStatusCode successStatus = HttpStatusCode.OK)
    {
        using var response = await SendRawAsync(method, path, body, cancellationToken);
        if (response.StatusCode != successStatus)
        {
            throw new MaxioApiException((int)response.StatusCode, operation);
        }

        return await ReadDocumentAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static MaxioPlan? ReadPlan(JsonElement product)
    {
        if (product.TryGetProperty("archived_at", out var archived) && archived.ValueKind != JsonValueKind.Null)
        {
            return null;
        }

        return new MaxioPlan(
            product.GetProperty("handle").GetString()!,
            product.GetProperty("name").GetString()!,
            GetString(product, "description"),
            GetInt64(product, "price_in_cents"),
            (int)GetInt64(product, "interval"),
            product.GetProperty("interval_unit").GetString()!);
    }

    private static MaxioSubscription ReadSubscription(JsonElement subscription)
    {
        var product = subscription.TryGetProperty("product", out var nestedProduct) && nestedProduct.ValueKind == JsonValueKind.Object
            ? nestedProduct
            : default;
        var productHandle = GetString(subscription, "product_handle") ?? GetString(product, "handle") ?? string.Empty;
        var planName = GetString(product, "name") ?? productHandle;
        var price = TryGetInt64(subscription, "product_price_in_cents") ?? TryGetInt64(product, "price_in_cents") ?? 0;
        return new MaxioSubscription(
            subscription.GetProperty("id").GetInt64(),
            GetString(subscription, "reference") ?? string.Empty,
            productHandle,
            planName,
            price,
            GetString(subscription, "state") ?? "unknown",
            GetDateTimeOffset(subscription, "next_assessment_at") ?? GetDateTimeOffset(subscription, "next_billing_at"));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static long GetInt64(JsonElement element, string name) => TryGetInt64(element, name) ?? 0;

    private static long? TryGetInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}
