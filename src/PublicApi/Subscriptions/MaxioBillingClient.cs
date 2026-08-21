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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly string _baseUrl;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        _baseUrl = _options.ResolveBaseUrl().TrimEnd('/');
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        const int pageSize = 200;
        var responses = new List<ProductResponse>();
        for (var page = 1; ; page++)
        {
            var currentPage = await SendAsync<List<ProductResponse>>(
                HttpMethod.Get,
                $"product_families/{family}/products.json?per_page={pageSize}&page={page}",
                null,
                cancellationToken);
            responses.AddRange(currentPage);
            if (currentPage.Count < pageSize)
            {
                break;
            }
        }

        return responses
            .Select(x => x.Product)
            .Where(x => x.ArchivedAt is null &&
                        string.Equals(x.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.PriceInCents)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var url = QueryHelpers.AddQueryString("customers/lookup.json", "reference", reference);
        var response = await SendOptionalAsync<CustomerResponse>(url, cancellationToken);
        return response is null ? null : new MaxioCustomer(response.Customer.Id, response.Customer.Reference);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest(new CreateCustomer(
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.Reference));
        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return new MaxioCustomer(response.Customer.Id, response.Customer.Reference);
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var url = QueryHelpers.AddQueryString("subscriptions/lookup.json", "reference", reference);
        var response = await SendOptionalAsync<SubscriptionResponse>(url, cancellationToken);
        return response is null ? null : ToSubscriptionDto(response.Subscription);
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionEnvelope(new CreateSubscription(
            productHandle,
            customerReference,
            subscriptionReference,
            "remittance"));
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return ToSubscriptionDto(response.Subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var responses = await SendAsync<List<SubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return responses
            .Select(x => x.Subscription)
            .Where(x => x.Product is not null &&
                        string.Equals(x.Product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(ToSubscriptionDto)
            .ToList();
    }

    private async Task<T?> SendOptionalAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        using var response = await SendCoreAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await SendCoreAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException("Maxio did not respond before the request timed out.", 504);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException("Maxio could not be reached.", 503, innerException: ex);
        }
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var providerMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(
                $"Maxio rejected the billing request with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                providerMessage);
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new MaxioApiException("Maxio returned an empty response.", 502);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException("Maxio returned an invalid JSON response.", 502, innerException: ex);
        }
    }

    private Uri BuildUri(string path) => new($"{_baseUrl}/{path.TrimStart('/')}");

    private static SubscriptionPlanDto ToPlanDto(Product product) => new(
        product.Handle,
        product.Name,
        product.Description ?? string.Empty,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.ProductPricePointName);

    private static SubscriptionDto ToSubscriptionDto(Subscription subscription)
    {
        var product = subscription.Product
            ?? throw new MaxioApiException("Maxio returned a subscription without a plan.", 502);

        return new SubscriptionDto(
            subscription.Id,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            product.ProductPricePointName,
            subscription.State,
            subscription.NextAssessmentAt);
    }

    private sealed record ProductResponse([property: JsonPropertyName("product")] Product Product);

    private sealed record Product(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("handle")] string Handle,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price_in_cents")] long PriceInCents,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("interval_unit")] string IntervalUnit,
        [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
        [property: JsonPropertyName("product_price_point_name")] string ProductPricePointName,
        [property: JsonPropertyName("product_family")] ProductFamily ProductFamily);

    private sealed record ProductFamily([property: JsonPropertyName("handle")] string Handle);

    private sealed record CustomerResponse([property: JsonPropertyName("customer")] Customer Customer);
    private sealed record Customer(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record CreateCustomerRequest([property: JsonPropertyName("customer")] CreateCustomer Customer);
    private sealed record CreateCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record SubscriptionResponse([property: JsonPropertyName("subscription")] Subscription Subscription);
    private sealed record Subscription(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
        [property: JsonPropertyName("product")] Product? Product);

    private sealed record CreateSubscriptionEnvelope(
        [property: JsonPropertyName("subscription")] CreateSubscription Subscription);
    private sealed record CreateSubscription(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_reference")] string CustomerReference,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);
}
