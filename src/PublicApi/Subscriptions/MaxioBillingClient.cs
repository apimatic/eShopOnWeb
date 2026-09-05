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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _options.GetProductFamilyHandle();
        var family = await GetAsync<ProductFamilyResponse>($"product_families/{EscapeHandle(familyHandle)}.json", cancellationToken);
        if (family.ProductFamily is null)
            throw new MaxioApiException(HttpStatusCode.BadGateway);

        var products = new List<MaxioPlan>();
        for (var page = 1; ; page++)
        {
            var pageItems = await GetAsync<List<ProductResponse>>(
                $"product_families/{family.ProductFamily.Id}/products.json?page={page}&per_page=200", cancellationToken);

            if (pageItems.Count == 0)
                break;

            products.AddRange(pageItems
                .Where(x => x.Product is { ArchivedAt: null })
                .Select(x => new MaxioPlan(
                    x.Product!.Handle,
                    x.Product.Name,
                    x.Product.Description,
                    x.Product.PriceInCents,
                    x.Product.Interval,
                    x.Product.IntervalUnit)));

            if (pageItems.Count < 200)
                break;
        }

        return products.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, "GET customers/lookup.json");
        var result = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions, cancellationToken);
        return result?.Customer is null ? throw new MaxioApiException(HttpStatusCode.BadGateway) : ToCustomer(result.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var localPart = email.Split('@', 2)[0];
        var request = new CustomerRequest(new CustomerCreateBody(localPart, "eShopOnWeb", email, reference));
        var result = await PostAsync<CustomerResponse>("customers.json", request, cancellationToken);
        return result.Customer is null ? throw new MaxioApiException(HttpStatusCode.BadGateway) : ToCustomer(result.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var result = await GetAsync<List<SubscriptionResponse>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return result.Where(x => x.Subscription is not null).Select(x => ToSubscription(x.Subscription!)).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken)
    {
        // Maxio's uniqueness_token is a POST parameter and rejects a duplicate submission with 409.
        var request = new SubscriptionCreateRequest(new SubscriptionCreateBody(customerId, productHandle));
        var result = await PostAsync<SubscriptionResponse>(
            $"subscriptions.json?uniqueness_token={Uri.EscapeDataString(uniquenessToken)}", request, cancellationToken);
        return result.Subscription is null ? throw new MaxioApiException(HttpStatusCode.BadGateway) : ToSubscription(result.Subscription);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, $"GET {path}");
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, JsonContent.Create(body, options: JsonOptions), cancellationToken);
        await EnsureSuccessAsync(response, $"POST {path.Split('?')[0]}");
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.GetApiKey()}:X"));
        var request = new HttpRequestMessage(method, new Uri(_options.GetBaseUri(), path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Deliberately do not copy the remote response into exceptions or API output.
            await response.Content.LoadIntoBufferAsync();
            _logger.LogWarning("Maxio returned status {StatusCode} for {Operation}", (int)response.StatusCode, operation);
            throw new MaxioApiException(response.StatusCode);
        }
    }

    private static string EscapeHandle(string handle) => Uri.EscapeDataString($"handle:{handle}");
    private static MaxioCustomer ToCustomer(MaxioCustomerBody customer) => new(customer.Id, customer.Reference, customer.Email);
    private static MaxioSubscription ToSubscription(MaxioSubscriptionBody subscription) => new(
        subscription.Id,
        subscription.State,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents,
        subscription.NextAssessmentAt);

    private sealed record ProductFamilyResponse([property: JsonPropertyName("product_family")] ProductFamilyBody? ProductFamily);
    private sealed record ProductFamilyBody([property: JsonPropertyName("id")] long Id);
    private sealed record ProductResponse([property: JsonPropertyName("product")] ProductBody? Product);
    private sealed record ProductBody(
        [property: JsonPropertyName("handle")] string Handle,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price_in_cents")] long PriceInCents,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("interval_unit")] string IntervalUnit,
        [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt);
    private sealed record CustomerResponse([property: JsonPropertyName("customer")] MaxioCustomerBody? Customer);
    private sealed record MaxioCustomerBody(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("email")] string? Email);
    private sealed record CustomerRequest([property: JsonPropertyName("customer")] CustomerCreateBody Customer);
    private sealed record CustomerCreateBody(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);
    private sealed record SubscriptionCreateRequest([property: JsonPropertyName("subscription")] SubscriptionCreateBody Subscription);
    private sealed record SubscriptionCreateBody(
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        // The seeded catalog accepts subscriptions without a card; remittance avoids an automatic collection attempt.
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod = "remittance");
    private sealed record SubscriptionResponse([property: JsonPropertyName("subscription")] MaxioSubscriptionBody? Subscription);
    private sealed record MaxioSubscriptionBody(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("product")] SubscriptionProductBody? Product,
        [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt);
    private sealed record SubscriptionProductBody(
        [property: JsonPropertyName("handle")] string? Handle,
        [property: JsonPropertyName("name")] string? Name);
}

public sealed record MaxioPlan(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record MaxioCustomer(long Id, string? Reference, string? Email);
public sealed record MaxioSubscription(long Id, string State, string? ProductHandle, string? ProductName, long ProductPriceInCents, DateTimeOffset? NextAssessmentAt);
